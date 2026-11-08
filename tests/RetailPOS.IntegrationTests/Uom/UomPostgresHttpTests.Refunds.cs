using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Uom;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Tests.Uom;

/// <summary>I02/I04: several refunds of one sale, each with its own identity, journal and exact share of the money.</summary>
public sealed partial class UomPostgresHttpTests
{
    private static object RefundBody(long variant, long itemId, decimal quantity, string key, string? condition = "resellable", bool packagingReusable = false) =>
        new
        {
            items = new[] { new { variantId = variant, quantity, saleItemId = itemId, condition, packagingReusable } },
            reason = "Customer return",
            idempotencyKey = key
        };

    private static Task<JsonElement> RefundAsync(HttpClient client, long saleId, object body) =>
        Send(client, HttpMethod.Post, $"/api/sales/{saleId}/refund", body);

    private async Task<SaleFinancialPosting[]> ReversalsAsync(long saleId)
    {
        SaleFinancialPosting[] rows = [];
        await _fixture.WithDbAsync(async db => rows = await db.SaleFinancialPostings.AsNoTracking()
            .Where(row => row.SaleId == saleId && row.Kind == SaleFinancialPostingKinds.Reversal)
            .OrderBy(row => row.Id)
            .ToArrayAsync());
        return rows;
    }

    private async Task PostAllAsync(HttpClient owner, long saleId)
    {
        long[] ids = [];
        await _fixture.WithDbAsync(async db => ids = await db.SaleFinancialPostings.AsNoTracking()
            .Where(row => row.SaleId == saleId)
            .OrderBy(row => row.Id)
            .Select(row => row.Id)
            .ToArrayAsync());
        foreach (var id in ids)
            await Send(owner, HttpMethod.Post, $"/api/financial-postings/sales/{id}/retry", new { });
    }

    private static decimal SnapshotAmount(SaleFinancialPosting posting, string property)
    {
        using var snapshot = JsonDocument.Parse(posting.SnapshotJson);
        return snapshot.RootElement.GetProperty(property).GetDecimal();
    }

    [Fact]
    public async Task Refunds_ThreePartials_AddUpExactly_EachWithItsOwnJournal_ThenTheSaleCloses()
    {
        var owner = Owner();
        var (attar, config) = await StockedAttarAsync(owner, "RF3");
        var sale = await SellAsync(owner, new SaleLine(attar, 1m, 100m, Option(config, "P12"), config.GetProperty("configId").GetInt64()));
        var saleId = sale.GetProperty("id").GetInt64();
        var itemId = sale.GetProperty("items")[0].GetProperty("id").GetInt64();

        var first = await RefundAsync(owner, saleId, RefundBody(attar, itemId, 5m, Unique("r")));
        Assert.Equal("partially_refunded", first.GetProperty("status").GetString());
        Assert.Equal(5m, first.GetProperty("items")[0].GetProperty("returnedQuantity").GetDecimal());
        Assert.Equal(7m, first.GetProperty("items")[0].GetProperty("remainingReturnableQuantity").GetDecimal());

        var second = await RefundAsync(owner, saleId, RefundBody(attar, itemId, 4m, Unique("r")));
        Assert.Equal("partially_refunded", second.GetProperty("status").GetString());
        using (var over = await owner.PostAsJsonAsync($"/api/sales/{saleId}/refund", RefundBody(attar, itemId, 4m, Unique("r"))))
            Assert.Equal(HttpStatusCode.BadRequest, over.StatusCode);

        var third = await RefundAsync(owner, saleId, RefundBody(attar, itemId, 3m, Unique("r")));
        Assert.Equal("refunded", third.GetProperty("status").GetString());
        Assert.Equal(0m, third.GetProperty("items")[0].GetProperty("remainingReturnableQuantity").GetDecimal());
        Assert.Equal(3, third.GetProperty("refunds").GetArrayLength());
        Assert.Equal(sale.GetProperty("saleNumber").GetString() + "-R2", third.GetProperty("refunds")[1].GetProperty("refundNumber").GetString());

        using (var after = await owner.PostAsJsonAsync($"/api/sales/{saleId}/refund", RefundBody(attar, itemId, 1m, Unique("r"))))
            Assert.Equal(HttpStatusCode.BadRequest, after.StatusCode);

        Assert.Equal(500m, await OnHandAsync(attar));

        var reversals = await ReversalsAsync(saleId);
        Assert.Equal(3, reversals.Length);
        Assert.Equal(3, reversals.Select(row => row.SourceKey).Distinct().Count());
        // ৳100 + 10% VAT, in shares that never lose or invent a cent: 41.67 + 33.33 + 25.00, and 4.17 + 3.33 + 2.50.
        Assert.Equal([41.67m, 33.33m, 25.00m], reversals.Select(row => SnapshotAmount(row, "refundAmount")));
        Assert.Equal([4.17m, 3.33m, 2.50m], reversals.Select(row => SnapshotAmount(row, "refundVat")));
        // 24 ml... of cost at ৳2/ml: 12 ml × ৳2 = ৳24, split by quantity.
        Assert.Equal(24m, reversals.Sum(row => SnapshotAmount(row, "cost")));

        await PostAllAsync(owner, saleId);
        await PostAllAsync(owner, saleId);
        await _fixture.WithDbAsync(async db =>
        {
            var journals = await db.JournalEntries.AsNoTracking()
                .Where(row => row.ReferenceType == "SaleRefund"
                    && (row.ReferenceId == saleId.ToString() || row.ReferenceId.StartsWith(saleId + ":R")))
                .ToListAsync();
            Assert.Equal(3, journals.Count);
            Assert.All(journals, journal => Assert.Equal(JournalEntryStatuses.Posted, journal.Status));
        });
    }

    [Fact]
    public async Task Refunds_MixedUnits_SameVariantOnTwoLines_FollowTheChosenLine()
    {
        var owner = Owner();
        var (attar, config) = await StockedAttarAsync(owner, "RMX");
        var version = config.GetProperty("configId").GetInt64();
        var plain = await _fixture.CreateVariantAsync("RMP");
        await ReceivePcsAsync(owner, plain, 10m, 50m);
        var sale = await SellAsync(
            owner,
            new SaleLine(attar, 2m, 100m, Option(config, "P12"), version),
            new SaleLine(attar, 10m, 5m, Option(config, "ML"), version),
            new SaleLine(plain, 3m, await VariantPriceAsync(owner, plain), null, null));
        var saleId = sale.GetProperty("id").GetInt64();
        var items = sale.GetProperty("items").EnumerateArray().ToArray();
        var pack = items.Single(row => row.GetProperty("quantity").GetDecimal() == 24m).GetProperty("id").GetInt64();
        var custom = items.Single(row => row.GetProperty("quantity").GetDecimal() == 10m).GetProperty("id").GetInt64();
        var pcs = items.Single(row => row.GetProperty("variantId").GetInt64() == plain).GetProperty("id").GetInt64();
        var attarBefore = await OnHandAsync(attar);

        // Which line is being returned must be said once a variant is on several lines.
        using (var vague = await owner.PostAsJsonAsync($"/api/sales/{saleId}/refund", new { items = new[] { new { variantId = attar, quantity = 1m } }, idempotencyKey = Unique("r") }))
            Assert.Equal(HttpStatusCode.BadRequest, vague.StatusCode);

        // One pack from the pack line, 2.5 ml of the custom line, and one piece.
        await RefundAsync(owner, saleId, new
        {
            items = new object[]
            {
                new { variantId = attar, quantity = 12m, saleItemId = pack, condition = "resellable" },
                new { variantId = attar, quantity = 2.5m, saleItemId = custom, condition = "resellable" },
                new { variantId = plain, quantity = 1m, saleItemId = pcs }
            },
            idempotencyKey = Unique("r")
        });
        Assert.Equal(attarBefore + 14.5m, await OnHandAsync(attar));

        // A plain pcs line stays whole, with the same safeguards.
        using (var half = await owner.PostAsJsonAsync($"/api/sales/{saleId}/refund", new { items = new[] { new { variantId = plain, quantity = 0.5m, saleItemId = pcs } }, idempotencyKey = Unique("r") }))
            Assert.Equal(HttpStatusCode.BadRequest, half.StatusCode);

        var last = await RefundAsync(owner, saleId, new
        {
            items = new object[]
            {
                new { variantId = attar, quantity = 12m, saleItemId = pack, condition = "resellable" },
                new { variantId = attar, quantity = 7.5m, saleItemId = custom, condition = "resellable" },
                new { variantId = plain, quantity = 2m, saleItemId = pcs }
            },
            idempotencyKey = Unique("r")
        });
        Assert.Equal("refunded", last.GetProperty("status").GetString());
        Assert.Equal(attarBefore + 34m, await OnHandAsync(attar));
        var reversals = await ReversalsAsync(saleId);
        Assert.Equal(sale.GetProperty("totalAmount").GetDecimal(), reversals.Sum(row => SnapshotAmount(row, "refundTotal")));
    }

    [Fact]
    public async Task Refunds_ARetryRepeatsNothing_AReusedKeyConflicts_AndAKeyIsNeededAfterTheFirst()
    {
        var owner = Owner();
        var (attar, config) = await StockedAttarAsync(owner, "RDP");
        var sale = await SellAsync(owner, new SaleLine(attar, 1m, 100m, Option(config, "P12"), config.GetProperty("configId").GetInt64()));
        var saleId = sale.GetProperty("id").GetInt64();
        var itemId = sale.GetProperty("items")[0].GetProperty("id").GetInt64();
        var key = Unique("r");

        await RefundAsync(owner, saleId, RefundBody(attar, itemId, 2m, key));
        var replay = await RefundAsync(owner, saleId, RefundBody(attar, itemId, 2m, key));
        Assert.Equal(1, replay.GetProperty("refunds").GetArrayLength());
        Assert.Equal(490m, await OnHandAsync(attar));
        Assert.Single(await ReversalsAsync(saleId));

        using (var conflict = await owner.PostAsJsonAsync($"/api/sales/{saleId}/refund", RefundBody(attar, itemId, 3m, key)))
        {
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
            Assert.Contains("IDEMPOTENCY_KEY_PAYLOAD_MISMATCH", await conflict.Content.ReadAsStringAsync());
        }

        using (var keyless = await owner.PostAsJsonAsync($"/api/sales/{saleId}/refund", new { items = new[] { new { variantId = attar, quantity = 1m, saleItemId = itemId, condition = "resellable" } } }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, keyless.StatusCode);
            Assert.Contains("already refunded", await keyless.Content.ReadAsStringAsync());
        }

        Assert.Equal(490m, await OnHandAsync(attar));
        Assert.Single(await ReversalsAsync(saleId));
    }

    [Fact]
    public async Task Refunds_Concurrent_NeverReturnMoreThanRemains_NorRepeatARetry()
    {
        var owner = Owner();
        var (attar, config) = await StockedAttarAsync(owner, "RCC");
        var sale = await SellAsync(owner, new SaleLine(attar, 1m, 100m, Option(config, "P12"), config.GetProperty("configId").GetInt64()));
        var saleId = sale.GetProperty("id").GetInt64();
        var itemId = sale.GetProperty("items")[0].GetProperty("id").GetInt64();

        // Six terminals each try to return 5 of the 12 ml: only two can.
        var attempts = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            using var client = Owner();
            using var response = await client.PostAsJsonAsync($"/api/sales/{saleId}/refund", RefundBody(attar, itemId, 5m, Unique("r")));
            return response.StatusCode;
        }));
        Assert.Equal(2, attempts.Count(status => status == HttpStatusCode.OK));
        Assert.DoesNotContain(HttpStatusCode.InternalServerError, attempts);
        Assert.Equal(498m, await OnHandAsync(attar));
        Assert.Equal(2, (await ReversalsAsync(saleId)).Length);

        // The same retried request, sent four times at once, takes effect once.
        var key = Unique("r");
        var retries = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            using var client = Owner();
            using var response = await client.PostAsJsonAsync($"/api/sales/{saleId}/refund", RefundBody(attar, itemId, 2m, key));
            return response.StatusCode;
        }));
        Assert.All(retries, status => Assert.Equal(HttpStatusCode.OK, status));
        Assert.Equal(500m, await OnHandAsync(attar));
        Assert.Equal(3, (await ReversalsAsync(saleId)).Length);

        await _fixture.WithDbAsync(async db =>
        {
            var item = await db.SaleItems.AsNoTracking().SingleAsync(row => row.Id == itemId);
            Assert.Equal(12m, item.ReturnedQuantity);
            Assert.Equal("refunded", (await db.Sales.AsNoTracking().SingleAsync(row => row.Id == saleId)).Status);
        });
    }

    [Fact]
    public async Task Refunds_TheDatabaseItselfRefusesReturningMoreThanWasSold()
    {
        var owner = Owner();
        var (attar, config) = await StockedAttarAsync(owner, "RCK");
        var sale = await SellAsync(owner, new SaleLine(attar, 1m, 100m, Option(config, "P12"), config.GetProperty("configId").GetInt64()));
        var itemId = sale.GetProperty("items")[0].GetProperty("id").GetInt64();

        await _fixture.WithDbAsync(async db =>
        {
            var error = await Assert.ThrowsAnyAsync<Exception>(() =>
                db.Database.ExecuteSqlInterpolatedAsync($"UPDATE sale_items SET returned_quantity = 13 WHERE id = {itemId}"));
            Assert.Contains("ck_sale_items_returned_quantity", error.Message);
        });
    }

    [Fact]
    public async Task LegacyExchangeLink_IsRejected_AndTheRefundEntitlementIsUntouched()
    {
        // Exchange D-01: the checkout link is refused; refund/exchange sharing is covered by the dedicated Exchange tests.
        var owner = Owner();
        var (attar, config) = await StockedAttarAsync(owner, "REX");
        var version = config.GetProperty("configId").GetInt64();
        var line = new SaleLine(attar, 1m, 100m, Option(config, "P12"), version);

        var sold = await SellAsync(owner, line);
        var soldId = sold.GetProperty("id").GetInt64();
        var soldItem = sold.GetProperty("items")[0].GetProperty("id").GetInt64();
        using (var linked = await owner.PostAsJsonAsync("/api/sales", ExchangeBody(soldId, line)))
        {
            Assert.Equal(HttpStatusCode.BadRequest, linked.StatusCode);
            Assert.Contains("SALE_EXCHANGE_LINK_DEPRECATED", await linked.Content.ReadAsStringAsync());
        }

        var closed = await RefundAsync(owner, soldId, RefundBody(attar, soldItem, 12m, Unique("r")));
        Assert.Equal("refunded", closed.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Refunds_ConditionDecidesStockAndCost_LineByLine()
    {
        var owner = Owner();
        var (attar, config) = await StockedAttarAsync(owner, "RCD");
        var sale = await SellAsync(owner, new SaleLine(attar, 1m, 100m, Option(config, "P12"), config.GetProperty("configId").GetInt64()));
        var saleId = sale.GetProperty("id").GetInt64();
        var itemId = sale.GetProperty("items")[0].GetProperty("id").GetInt64();

        await RefundAsync(owner, saleId, RefundBody(attar, itemId, 6m, Unique("r"), "non_resellable"));
        Assert.Equal(488m, await OnHandAsync(attar));
        var details = await RefundAsync(owner, saleId, RefundBody(attar, itemId, 6m, Unique("r")));
        Assert.Equal(494m, await OnHandAsync(attar));

        var reversals = await ReversalsAsync(saleId);
        Assert.Equal([0m, 12m], reversals.Select(row => SnapshotAmount(row, "cost")));
        Assert.Equal([50m, 50m], reversals.Select(row => SnapshotAmount(row, "refundAmount")));

        // The history says what happened to each return, and shows no cost.
        var history = details.GetProperty("refunds");
        Assert.Equal("non_resellable", history[0].GetProperty("lines")[0].GetProperty("condition").GetString());
        Assert.Equal(0m, history[0].GetProperty("lines")[0].GetProperty("restockedQuantity").GetDecimal());
        Assert.False(history[0].TryGetProperty("costRestored", out _));
    }

    [Fact]
    public async Task Refunds_WithDiscountAndVat_ReturnExactlyWhatWasPaid()
    {
        var owner = Owner();
        var (attar, config) = await StockedAttarAsync(owner, "RDV");
        var option = Option(config, "P12");
        // 3 packs at ৳100, ৳10 off the line: net ৳290, VAT ৳29, paid ৳319.
        var sale = await Send(owner, HttpMethod.Post, "/api/sales", new
        {
            outletId = UomPostgresFixture.Outlet,
            terminalId = UomPostgresFixture.Terminal,
            discount = 0m,
            tax = 29m,
            paymentMethod = "card",
            idempotencyKey = Unique("sale"),
            payments = new[] { new { method = "card", amount = 319m } },
            items = new[]
            {
                new
                {
                    variantId = attar, quantity = 3m, unitPrice = 100m, discountAmount = 10m,
                    sellingOptionId = option, expectedConfigVersionId = config.GetProperty("configId").GetInt64()
                }
            }
        });
        var saleId = sale.GetProperty("id").GetInt64();
        var itemId = sale.GetProperty("items")[0].GetProperty("id").GetInt64();

        foreach (var _ in Enumerable.Range(0, 3))
            await RefundAsync(owner, saleId, RefundBody(attar, itemId, 12m, Unique("r")));

        var reversals = await ReversalsAsync(saleId);
        Assert.Equal([96.67m, 96.66m, 96.67m], reversals.Select(row => SnapshotAmount(row, "refundAmount")));
        Assert.Equal(29m, reversals.Sum(row => SnapshotAmount(row, "refundVat")));
        Assert.Equal(319m, reversals.Sum(row => SnapshotAmount(row, "refundTotal")));
    }

    [Fact]
    public async Task Refunds_SplitPayments_PayBackEachMethodInFull_AndTheDrawerMatchesTheJournal()
    {
        var owner = Owner();
        var (attar, config) = await StockedAttarAsync(owner, "RCS");
        var sale = await Send(owner, HttpMethod.Post, "/api/sales", new
        {
            outletId = UomPostgresFixture.Outlet,
            terminalId = UomPostgresFixture.Terminal,
            discount = 0m,
            tax = 10m,
            paymentMethod = "split",
            idempotencyKey = Unique("sale"),
            payments = new object[]
            {
                new { method = "cash", amount = 33m, tendered = 33m },
                new { method = "card", amount = 77m }
            },
            items = new[]
            {
                new
                {
                    variantId = attar, quantity = 1m, unitPrice = 100m, discountAmount = 0m,
                    sellingOptionId = Option(config, "P12"), expectedConfigVersionId = config.GetProperty("configId").GetInt64()
                }
            }
        });
        var saleId = sale.GetProperty("id").GetInt64();
        var itemId = sale.GetProperty("items")[0].GetProperty("id").GetInt64();

        // A stated cash amount can only confirm the cash share, never change it.
        using (var wrong = await owner.PostAsJsonAsync($"/api/sales/{saleId}/refund", new
        {
            items = new[] { new { variantId = attar, quantity = 5m, saleItemId = itemId, condition = "resellable" } },
            cashRefundAmount = 1m,
            idempotencyKey = Unique("r")
        }))
            Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Equal(488m, await OnHandAsync(attar));

        var details = await RefundAsync(owner, saleId, RefundBody(attar, itemId, 5m, Unique("r")));
        await RefundAsync(owner, saleId, RefundBody(attar, itemId, 4m, Unique("r")));
        details = await RefundAsync(owner, saleId, RefundBody(attar, itemId, 3m, Unique("r")));

        var refunds = details.GetProperty("refunds").EnumerateArray().ToArray();
        var perMethod = refunds
            .SelectMany(refund => refund.GetProperty("payments").EnumerateArray())
            .GroupBy(payment => payment.GetProperty("method").GetString()!)
            .ToDictionary(group => group.Key, group => group.Sum(payment => payment.GetProperty("amount").GetDecimal()));
        Assert.Equal(33m, perMethod["cash"]);
        Assert.Equal(77m, perMethod["card"]);
        Assert.Equal(33m, refunds.Sum(refund => refund.GetProperty("cashAmount").GetDecimal()));

        await _fixture.WithDbAsync(async db =>
        {
            var movements = await db.CashSessionMovements.AsNoTracking()
                .Where(row => row.OriginalSaleId == saleId && row.MovementType == CashSessionMovementTypes.CashRefund)
                .ToListAsync();
            Assert.Equal(3, movements.Count);
            Assert.Equal(-33m, movements.Sum(row => row.CashEffect));
        });
    }

    [Fact]
    public async Task Refunds_LegacyPcsSale_UsesTheSameWorkflow_WithoutACondition()
    {
        var owner = Owner();
        var variant = await _fixture.CreateVariantAsync("RLG");
        await ReceivePcsAsync(owner, variant, 10m, 20m);
        var price = await VariantPriceAsync(owner, variant);
        var sale = await SellAsync(owner, new SaleLine(variant, 3m, price, null, null));
        var saleId = sale.GetProperty("id").GetInt64();
        var itemId = sale.GetProperty("items")[0].GetProperty("id").GetInt64();

        await RefundAsync(owner, saleId, new { items = new[] { new { variantId = variant, quantity = 1m, saleItemId = itemId } }, idempotencyKey = Unique("r") });
        var last = await RefundAsync(owner, saleId, new { items = new[] { new { variantId = variant, quantity = 2m, saleItemId = itemId } }, idempotencyKey = Unique("r") });

        Assert.Equal("refunded", last.GetProperty("status").GetString());
        Assert.Equal(10m, await OnHandAsync(variant));
        var sold = last.GetProperty("items")[0];
        Assert.Equal(3, sold.GetProperty("quantity").GetDecimal());
        Assert.Equal(sale.GetProperty("totalAmount").GetDecimal(), (await ReversalsAsync(saleId)).Sum(row => SnapshotAmount(row, "refundTotal")));
    }

    [Fact]
    public async Task Refunds_SaleRefundedBeforeThisChange_StaysClosed_AndKeepsItsJournalIdentity()
    {
        var owner = Owner();
        var (attar, config) = await StockedAttarAsync(owner, "RHS");
        var sale = await SellAsync(owner, new SaleLine(attar, 1m, 100m, Option(config, "P12"), config.GetProperty("configId").GetInt64()));
        var saleId = sale.GetProperty("id").GetInt64();
        var itemId = sale.GetProperty("items")[0].GetProperty("id").GetInt64();

        // What an old partial refund left behind: status refunded, and no refund row.
        await _fixture.WithDbAsync(db => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE sales SET status = 'refunded' WHERE id = {saleId}"));

        using (var refused = await owner.PostAsJsonAsync($"/api/sales/{saleId}/refund", RefundBody(attar, itemId, 1m, Unique("r"))))
        {
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Contains("already refunded", await refused.Content.ReadAsStringAsync());
        }

        var read = await Data(owner, $"/api/sales/{saleId}");
        Assert.Equal(0m, read.GetProperty("items")[0].GetProperty("remainingReturnableQuantity").GetDecimal());
        Assert.Equal("sale:1:reversal:v1", RetailPOS.Application.Sales.FinancialPostings.SaleFinancialIntentFactory.ReversalSourceKey(1));
    }

    [Fact]
    public async Task Refund_AfterTrackingIsSwitchedOff_ReturnsStockWithoutPackStateMovements()
    {
        var owner = Owner();
        var attar = await _fixture.CreateVariantAsync("ROF");
        var config = await SaveConfigAsync(owner, attar, "ml", Jar500(owner), [Pack("P12", 12m, 100m), CustomMl(5m)], packTracking: true);
        await PurchaseAsync(owner, attar, config.GetProperty("purchaseOptions")[0].GetProperty("id").GetInt64(), 1m, 1000m);
        var sale = await SellAsync(owner, new SaleLine(attar, 1m, 100m, Option(config, "P12"), config.GetProperty("configId").GetInt64(), "loose"));
        var saleId = sale.GetProperty("id").GetInt64();
        var itemId = sale.GetProperty("items")[0].GetProperty("id").GetInt64();

        await SaveConfigAsync(owner, attar, "ml", Jar500(owner), [Pack("P12", 12m, 100m), CustomMl(5m)], packTracking: false);
        var result = await RefundAsync(owner, saleId, RefundBody(attar, itemId, 12m, Unique("r")));

        Assert.Equal("refunded", result.GetProperty("status").GetString());
        Assert.Equal(500m, await OnHandAsync(attar));
    }

    [Fact]
    public async Task Refunds_KeepTheBooksInStep_ThroughPurchaseSalePartialAndDamagedReturns()
    {
        var owner = Owner();
        var before = await ValuationReportAsync(owner);
        var (attar, config) = await StockedAttarAsync(owner, "RGL");
        var sale = await SellAsync(owner, new SaleLine(attar, 2m, 100m, Option(config, "P12"), config.GetProperty("configId").GetInt64()));
        var saleId = sale.GetProperty("id").GetInt64();
        var itemId = sale.GetProperty("items")[0].GetProperty("id").GetInt64();

        await RefundAsync(owner, saleId, RefundBody(attar, itemId, 5m, Unique("r")));
        await RefundAsync(owner, saleId, RefundBody(attar, itemId, 7m, Unique("r"), "non_resellable"));
        await RefundAsync(owner, saleId, RefundBody(attar, itemId, 12m, Unique("r")));
        await PostAllAsync(owner, saleId);

        var after = await ValuationReportAsync(owner);
        // ৳1000 bought, 24 ml × ৳2 sold out, 17 ml × ৳2 back. The 7 damaged ml stay expensed.
        Assert.Equal(before.Value + 1000m - 48m + 34m, after.Value);
        Assert.Equal(before.Value - before.Gl, after.Value - after.Gl);
    }

    private static object ExchangeBody(long originalSaleId, SaleLine line)
    {
        var net = line.Quantity * line.UnitPrice;
        var tax = Math.Round(net * 0.10m, 2, MidpointRounding.AwayFromZero);
        return new
        {
            outletId = UomPostgresFixture.Outlet,
            terminalId = UomPostgresFixture.Terminal,
            discount = 0m,
            tax,
            paymentMethod = "card",
            idempotencyKey = Unique("sale"),
            originalSaleId,
            payments = new[] { new { method = "card", amount = net + tax } },
            items = new[]
            {
                new
                {
                    variantId = line.VariantId, quantity = line.Quantity, unitPrice = line.UnitPrice, discountAmount = 0m,
                    sellingOptionId = line.OptionId, expectedConfigVersionId = line.Version
                }
            }
        };
    }

    private static async Task<decimal> VariantPriceAsync(HttpClient owner, long variant)
    {
        using var response = await owner.PostAsJsonAsync("/api/pos/cart-prices", new
        {
            items = new[] { new { productVariantId = variant, quantity = 1, outletId = UomPostgresFixture.Outlet } }
        });
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, text);
        using var json = JsonDocument.Parse(text);
        return json.RootElement.GetProperty("data")[0].GetProperty("finalPrice").GetDecimal();
    }
}
