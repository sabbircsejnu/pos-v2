using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace RetailPOS.Tests.Uom;

/// <summary>I03: automatic pricing rules on selling options, through the existing pricing engine.</summary>
public sealed partial class UomPostgresHttpTests
{
    private static object PackWithRules(string code, decimal baseQuantity, decimal price, bool? rules) =>
        new { kind = "pack", code, label = $"{baseQuantity} ml pack", unitId = (long?)null, baseQuantity = (decimal?)baseQuantity, price, isDefault = false, sealedPackCode = (string?)null, isPreparedEligible = false, components = Array.Empty<object>(), applyAutomaticPricingRules = rules };

    private static object CustomWithRules(decimal price, bool? rules) =>
        new { kind = "custom", code = "ML", label = "Custom ml", unitId = (long?)UomPostgresFixture.MlUnitId, baseQuantity = (decimal?)null, price, isDefault = false, sealedPackCode = (string?)null, isPreparedEligible = false, components = Array.Empty<object>(), applyAutomaticPricingRules = rules };

    private static Task CreateRuleAsync(HttpClient owner, long variant, string discountType, decimal value, int minQuantity = 1, string name = "Promo") =>
        Send(owner, HttpMethod.Post, "/api/pricing/rules", new
        {
            name = $"{name} {variant} {discountType} {value} {minQuantity}",
            ruleType = "variant",
            targetId = variant,
            discountType,
            discountValue = value,
            minQuantity,
            priority = 10,
            isActive = true
        });

    private async Task<(long Variant, JsonElement Config)> PromoAttarAsync(HttpClient owner, string code, bool? packRules, bool? customRules)
    {
        var attar = await _fixture.CreateVariantAsync(code);
        var config = await SaveConfigAsync(owner, attar, "ml", Jar500(owner), [PackWithRules("P12", 12m, 100m, packRules), CustomWithRules(5m, customRules)]);
        await PurchaseAsync(owner, attar, config.GetProperty("purchaseOptions")[0].GetProperty("id").GetInt64(), 1m, 1000m);
        return (attar, config);
    }

    private static async Task<JsonElement> CartPriceAsync(HttpClient owner, long variant, long option, decimal entered)
    {
        using var response = await owner.PostAsJsonAsync("/api/pos/cart-prices", new
        {
            items = new[] { new { productVariantId = variant, quantity = 1, outletId = UomPostgresFixture.Outlet, sellingOptionId = option, enteredQuantity = entered } }
        });
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, text);
        using var json = JsonDocument.Parse(text);
        return json.RootElement.GetProperty("data")[0].Clone();
    }

    private static object PromoSaleBody(long variant, decimal quantity, decimal unitPrice, decimal gross, long option, long version, decimal? expectedGross = null, decimal lineDiscount = 0m)
    {
        var net = gross - lineDiscount;
        var tax = Math.Round(net * 0.10m, 2, MidpointRounding.AwayFromZero);
        return new
        {
            outletId = UomPostgresFixture.Outlet,
            terminalId = UomPostgresFixture.Terminal,
            discount = 0m,
            tax,
            paymentMethod = "card",
            idempotencyKey = Unique("sale"),
            payments = new[] { new { method = "card", amount = net + tax } },
            items = new[]
            {
                new
                {
                    variantId = variant, quantity, unitPrice, discountAmount = lineDiscount,
                    sellingOptionId = option, expectedConfigVersionId = version, expectedLineGross = expectedGross
                }
            }
        };
    }

    [Fact]
    public async Task Promotions_DefaultOffForPacksOnForCustom_AndAnUnsaidFlagKeepsWhatTheOptionHad()
    {
        var owner = Owner();
        var attar = await _fixture.CreateVariantAsync("PDF");
        var config = await SaveConfigAsync(owner, attar, "ml", Jar500(owner), [Pack("P12", 12m, 100m), CustomMl(5m)]);
        bool Flag(JsonElement current, string code) => current.GetProperty("sellingOptions").EnumerateArray()
            .Single(row => row.GetProperty("code").GetString() == code).GetProperty("applyAutomaticPricingRules").GetBoolean();
        Assert.False(Flag(config, "P12"));
        Assert.True(Flag(config, "ML"));

        // An old option that was off stays off when a later save says nothing about it, and the reverse.
        var changed = await SaveConfigAsync(owner, attar, "ml", Jar500(owner), [PackWithRules("P12", 12m, 100m, true), CustomWithRules(5m, false)]);
        Assert.True(Flag(changed, "P12"));
        Assert.False(Flag(changed, "ML"));
        var silent = await SaveConfigAsync(owner, attar, "ml", Jar500(owner), [Pack("P12", 12m, 90m), CustomMl(4m)]);
        Assert.True(Flag(silent, "P12"));
        Assert.False(Flag(silent, "ML"));
    }

    [Fact]
    public async Task Promotions_AnIneligiblePack_KeepsItsPrice_AndNoRuleIsRecorded()
    {
        var owner = Owner();
        var (attar, config) = await PromoAttarAsync(owner, "POF", false, false);
        await CreateRuleAsync(owner, attar, "percentage", 50m);

        var quote = await CartPriceAsync(owner, attar, Option(config, "P12"), 1m);
        Assert.Equal(100m, quote.GetProperty("lineGross").GetDecimal());
        Assert.Equal(JsonValueKind.Null, quote.GetProperty("appliedRuleName").ValueKind);

        var sale = await SellAsync(owner, new SaleLine(attar, 1m, 100m, Option(config, "P12"), config.GetProperty("configId").GetInt64()));
        Assert.Equal(110m, sale.GetProperty("totalAmount").GetDecimal());
        Assert.Equal(JsonValueKind.Null, sale.GetProperty("items")[0].GetProperty("appliedRuleName").ValueKind);
    }

    [Fact]
    public async Task Promotions_APackMinimum_CountsPacks_NeverMillilitres()
    {
        var owner = Owner();
        var (attar, config) = await PromoAttarAsync(owner, "PMN", true, false);
        var option = Option(config, "P12");
        await CreateRuleAsync(owner, attar, "percentage", 10m, minQuantity: 3);

        // Two packs hold 24 ml, far past the minimum of 3. That is not what the rule counts.
        var two = await CartPriceAsync(owner, attar, option, 2m);
        Assert.Equal(200m, two.GetProperty("lineGross").GetDecimal());
        Assert.Equal(JsonValueKind.Null, two.GetProperty("appliedRuleName").ValueKind);

        var three = await CartPriceAsync(owner, attar, option, 3m);
        Assert.Equal(90m, three.GetProperty("finalPrice").GetDecimal());
        Assert.Equal(270m, three.GetProperty("lineGross").GetDecimal());
        Assert.NotEqual(JsonValueKind.Null, three.GetProperty("appliedRuleName").ValueKind);

        var version = config.GetProperty("configId").GetInt64();
        var sale = await Send(owner, HttpMethod.Post, "/api/sales", PromoSaleBody(attar, 3m, 90m, 270m, option, version, 270m));
        Assert.Equal(297m, sale.GetProperty("totalAmount").GetDecimal());
        var line = sale.GetProperty("items")[0];
        Assert.NotEqual(JsonValueKind.Null, line.GetProperty("appliedRuleName").ValueKind);
        Assert.Equal(90m, line.GetProperty("enteredUnitPrice").GetDecimal());
        Assert.Equal(36m, line.GetProperty("quantity").GetDecimal());
    }

    [Fact]
    public async Task Promotions_AFixedRule_TakesItsAmountOffEachPack()
    {
        var owner = Owner();
        var (attar, config) = await PromoAttarAsync(owner, "PFX", true, false);
        await CreateRuleAsync(owner, attar, "fixed", 15m);

        var quote = await CartPriceAsync(owner, attar, Option(config, "P12"), 2m);
        Assert.Equal(85m, quote.GetProperty("finalPrice").GetDecimal());
        Assert.Equal(170m, quote.GetProperty("lineGross").GetDecimal());
    }

    [Fact]
    public async Task Promotions_ACustomQuantity_TakesAPercentage_AndNamesARuleItCannotApply()
    {
        var owner = Owner();
        var (attar, config) = await PromoAttarAsync(owner, "PCU", false, true);
        var option = Option(config, "ML");
        var version = config.GetProperty("configId").GetInt64();

        await CreateRuleAsync(owner, attar, "percentage", 10m, name: "Tenth");
        var quote = await CartPriceAsync(owner, attar, option, 7m);
        // 7 ml × ৳5 = ৳35, less 10% on the line. The unit price stays the option's, and the line is the authority.
        Assert.Equal(5m, quote.GetProperty("finalPrice").GetDecimal());
        Assert.Equal(31.5m, quote.GetProperty("lineGross").GetDecimal());

        var sale = await Send(owner, HttpMethod.Post, "/api/sales", PromoSaleBody(attar, 7m, 5m, 31.5m, option, version, 31.5m));
        Assert.Equal(34.65m, sale.GetProperty("totalAmount").GetDecimal());

        // A fixed amount per what? A minimum of 5 what? Neither can be answered for millilitres.
        await CreateRuleAsync(owner, attar, "fixed", 1m, name: "Flat");
        await CreateRuleAsync(owner, attar, "percentage", 50m, minQuantity: 5, name: "Bulk");
        var ambiguous = await CartPriceAsync(owner, attar, option, 7m);
        Assert.Equal(31.5m, ambiguous.GetProperty("lineGross").GetDecimal());
        var notApplied = ambiguous.GetProperty("rulesNotApplied").EnumerateArray().Select(row => row.GetString()!).ToArray();
        Assert.Equal(2, notApplied.Length);
        Assert.Contains(notApplied, name => name.StartsWith("Flat"));
        Assert.Contains(notApplied, name => name.StartsWith("Bulk"));
    }

    [Fact]
    public async Task Promotions_ManualDiscountAndVat_ApplyAfterTheRule()
    {
        var owner = Owner();
        var (attar, config) = await PromoAttarAsync(owner, "PMD", true, false);
        var option = Option(config, "P12");
        await CreateRuleAsync(owner, attar, "percentage", 10m);

        var sale = await Send(owner, HttpMethod.Post, "/api/sales", PromoSaleBody(attar, 2m, 90m, 180m, option, config.GetProperty("configId").GetInt64(), lineDiscount: 20m));
        Assert.Equal(176m, sale.GetProperty("totalAmount").GetDecimal());
        Assert.Equal(16m, sale.GetProperty("tax").GetDecimal());
    }

    [Fact]
    public async Task Promotions_AChangeSinceTheCartWasPriced_RefusesTheSale()
    {
        var owner = Owner();
        var (attar, config) = await PromoAttarAsync(owner, "PST", true, true);
        var version = config.GetProperty("configId").GetInt64();

        // The cashier saw list prices. A rule then starts before they pay.
        await CreateRuleAsync(owner, attar, "percentage", 10m);

        using var stalePack = await owner.PostAsJsonAsync("/api/sales", PromoSaleBody(attar, 1m, 100m, 100m, Option(config, "P12"), version, 100m));
        Assert.Equal(HttpStatusCode.Conflict, stalePack.StatusCode);
        Assert.Contains("SALE_PRICE_CHANGED", await stalePack.Content.ReadAsStringAsync());

        using var staleCustom = await owner.PostAsJsonAsync("/api/sales", PromoSaleBody(attar, 7m, 5m, 35m, Option(config, "ML"), version, 35m));
        Assert.Equal(HttpStatusCode.Conflict, staleCustom.StatusCode);
        Assert.Contains("SALE_PRICE_CHANGED", await staleCustom.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Promotions_ACommittedSale_KeepsWhatItPaid_AndARefundFollowsIt()
    {
        var owner = Owner();
        var (attar, config) = await PromoAttarAsync(owner, "PHS", true, false);
        var option = Option(config, "P12");
        var version = config.GetProperty("configId").GetInt64();
        await CreateRuleAsync(owner, attar, "percentage", 10m);

        var sale = await Send(owner, HttpMethod.Post, "/api/sales", PromoSaleBody(attar, 1m, 90m, 90m, option, version, 90m));
        var saleId = sale.GetProperty("id").GetInt64();
        var itemId = sale.GetProperty("items")[0].GetProperty("id").GetInt64();

        await SaveConfigAsync(owner, attar, "ml", Jar500(owner), [PackWithRules("P12", 12m, 500m, false), CustomWithRules(50m, false)]);
        await RefundAsync(owner, saleId, RefundBody(attar, itemId, 12m, Unique("r")));

        var reversal = Assert.Single(await ReversalsAsync(saleId));
        Assert.Equal(90m, SnapshotAmount(reversal, "refundAmount"));
        Assert.Equal(9m, SnapshotAmount(reversal, "refundVat"));
    }

    [Fact]
    public async Task Promotions_LegacyPcs_RulesKeepWorkingAsBefore()
    {
        var owner = Owner();
        var variant = await _fixture.CreateVariantAsync("PLG");
        await ReceivePcsAsync(owner, variant, 10m, 20m);
        var price = await VariantPriceAsync(owner, variant);
        await CreateRuleAsync(owner, variant, "percentage", 10m, minQuantity: 3);

        Assert.Equal(price, await VariantPriceAsync(owner, variant));
        using var response = await owner.PostAsJsonAsync("/api/pos/cart-prices", new
        {
            items = new[] { new { productVariantId = variant, quantity = 3, outletId = UomPostgresFixture.Outlet } }
        });
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var row = json.RootElement.GetProperty("data")[0];
        Assert.Equal(Math.Round(price * 0.9m, 2, MidpointRounding.AwayFromZero), row.GetProperty("finalPrice").GetDecimal());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("lineGross").ValueKind);
    }
}
