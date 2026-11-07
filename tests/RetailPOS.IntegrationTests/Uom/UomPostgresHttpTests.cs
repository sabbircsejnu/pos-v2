using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Uom;
using RetailPOS.Infrastructure.AccountingConfigurations;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;
using RetailPOS.Tests.Sales;

namespace RetailPOS.Tests.Uom;

/// <summary>
/// The Attar scenarios of the UOM SRS through the real HTTP pipeline, real DI and real migrations on PostgreSQL.
/// Each test builds its own variants, so tests stay independent inside the shared database.
/// </summary>
public sealed partial class UomPostgresHttpTests : IClassFixture<UomPostgresFixture>
{
    private readonly UomPostgresFixture _fixture;

    public UomPostgresHttpTests(UomPostgresFixture fixture) => _fixture = fixture;

    private HttpClient Owner() => _fixture.Client(UomPostgresFixture.OwnerId, "BusinessOwner", "*");

    [Fact]
    public async Task Migration_SeedsUnits_AndLegacyQuantitiesReadBackAsIntegers()
    {
        var catalog = await Data(Owner(), "/api/v1/uom/units");
        var codes = catalog.GetProperty("units").EnumerateArray().Select(unit => unit.GetProperty("code").GetString()).ToList();
        Assert.Contains("pcs", codes);
        Assert.Contains("ml", codes);
        Assert.Contains("kg", codes);
        Assert.Equal(3, catalog.GetProperty("conversions").GetArrayLength());

        await _fixture.WithDbAsync(async db =>
        {
            var quantity = await db.Inventories.AsNoTracking().Where(row => row.VariantId == UomPostgresFixture.LegacyVariant).Select(row => row.Quantity).SingleAsync();
            Assert.Equal("40", quantity.ToString(System.Globalization.CultureInfo.InvariantCulture));
        });
    }

    [Fact]
    public async Task Catalog_RejectsDuplicates_AndProtectsUsedUnits()
    {
        var owner = Owner();
        using var duplicate = await owner.PostAsJsonAsync("/api/v1/uom/units", new { code = "ML", name = "Dup", symbol = "xx", dimension = "volume", quantityPrecision = 2, isWholeUnit = false });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var code = Unique("tin");
        var created = await Send(owner, HttpMethod.Post, "/api/v1/uom/units", new { code, name = "Tin", symbol = code, dimension = "packaging", quantityPrecision = 0, isWholeUnit = true });
        var unitId = created.GetProperty("id").GetInt64();

        using var badPrecision = await owner.PostAsJsonAsync("/api/v1/uom/units", new { code = Unique("x"), name = "Bad", symbol = Unique("y"), dimension = "mass", quantityPrecision = 9, isWholeUnit = false });
        Assert.Equal(HttpStatusCode.BadRequest, badPrecision.StatusCode);

        var variant = await _fixture.CreateVariantAsync("TIN");
        await SaveConfigAsync(owner, variant, "ml", purchase: [new { code = "TIN1", unitId, label = "1 Tin", factorNumerator = 100m, factorDenominator = 1m, isDefault = true, isSealedPack = false }], selling: [CustomMl(5m)]);
        using var incompatible = await owner.PutAsJsonAsync($"/api/v1/uom/units/{unitId}", new { code, name = "Tin", symbol = code, dimension = "mass", quantityPrecision = 0, isWholeUnit = true, isActive = true });
        Assert.Equal(HttpStatusCode.Conflict, incompatible.StatusCode);
        using var deactivate = await owner.PutAsJsonAsync($"/api/v1/uom/units/{unitId}", new { code, name = "Tin", symbol = code, dimension = "packaging", quantityPrecision = 0, isWholeUnit = true, isActive = false });
        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);
    }

    [Fact]
    public async Task Config_BaseUnitLocksAfterActivity_DimensionMismatchFails_AndVersionsAreKept()
    {
        var owner = Owner();
        using var locked = await PutConfigAsync(owner, UomPostgresFixture.LegacyVariant, "ml", [], [CustomMl(5m)]);
        Assert.Equal(HttpStatusCode.Conflict, locked.StatusCode);
        Assert.Contains(UomErrorCode("BASE_UNIT_LOCKED"), await locked.Content.ReadAsStringAsync());

        var variant = await _fixture.CreateVariantAsync("CFG");
        var kg = await UnitIdAsync(owner, "kg");
        using var mismatch = await PutConfigAsync(owner, variant, "ml", [], [new { kind = "custom", code = "KG", label = "per kg", unitId = kg, baseQuantity = (decimal?)null, price = 5m, isDefault = false, sealedPackCode = (string?)null, isPreparedEligible = false, components = Array.Empty<object>() }]);
        Assert.Equal(HttpStatusCode.BadRequest, mismatch.StatusCode);
        Assert.Contains(UomErrorCode("DIMENSION_MISMATCH"), await mismatch.Content.ReadAsStringAsync());

        await SaveConfigAsync(owner, variant, "ml", [], [CustomMl(5m)]);
        await SaveConfigAsync(owner, variant, "ml", [], [CustomMl(6m)]);
        var config = await Data(owner, $"/api/v1/uom/variants/{variant}/config");
        Assert.Equal(2, config.GetProperty("current").GetProperty("version").GetInt32());
        var history = config.GetProperty("history").EnumerateArray().ToList();
        Assert.Equal(2, history.Count);
        Assert.True(history[1].GetProperty("effectiveToUtc").ValueKind != JsonValueKind.Null);
    }

    [Fact]
    public async Task PurchaseOneJar_ReceivesBaseQuantityAtExactCost_AndJournalMatchesLedger()
    {
        var owner = Owner();
        var attar = await _fixture.CreateVariantAsync("ATR");
        var config = await SaveConfigAsync(owner, attar, "ml", Jar500(owner), [Pack("P12", 12m, 100m), CustomMl(5m)]);
        var option = config.GetProperty("purchaseOptions")[0];
        var debitBefore = await InventoryDebitsAsync();

        var po = await PurchaseAsync(owner, attar, option.GetProperty("id").GetInt64(), quantity: 1m, unitPrice: 1000m);
        var line = po.GetProperty("purchaseOrder").GetProperty("items")[0];
        Assert.Equal(500m, line.GetProperty("quantity").GetDecimal());
        Assert.Equal(2m, line.GetProperty("unitPrice").GetDecimal());
        Assert.Equal(1m, line.GetProperty("uomEnteredQuantity").GetDecimal());
        Assert.Equal(1000m, line.GetProperty("uomEnteredUnitPrice").GetDecimal());

        await _fixture.WithDbAsync(async db =>
        {
            var inventory = await db.Inventories.AsNoTracking().SingleAsync(row => row.VariantId == attar);
            Assert.Equal(500m, inventory.Quantity);
            var ledger = await db.StockLedgers.AsNoTracking().SingleAsync(row => row.VariantId == attar);
            Assert.Equal(500m, ledger.QtyIn);
            Assert.Equal(1000m, ledger.MovementValue);
            Assert.True(ledger.IsCostValuationReliable);
        });
        Assert.Equal(debitBefore + 1000m, await InventoryDebitsAsync());
    }

    [Fact]
    public async Task PriceThatDoesNotDivideEvenly_StillPostsTheExactLineAmount()
    {
        var owner = Owner();
        var attar = await _fixture.CreateVariantAsync("ATR3");
        var jar = await UnitIdAsync(owner, "jar");
        var config = await SaveConfigAsync(owner, attar, "ml",
            [new { code = "J333", unitId = jar, label = "333 ml Jar", factorNumerator = 333m, factorDenominator = 1m, isDefault = true, isSealedPack = false }],
            [CustomMl(5m)]);
        await PurchaseAsync(owner, attar, config.GetProperty("purchaseOptions")[0].GetProperty("id").GetInt64(), 1m, 1000m);
        await _fixture.WithDbAsync(async db =>
        {
            var ledger = await db.StockLedgers.AsNoTracking().SingleAsync(row => row.VariantId == attar);
            Assert.Equal(1000m, ledger.MovementValue);
            Assert.Equal(333m, ledger.QtyIn);
        });
    }

    [Fact]
    public async Task PosSale_FixedPackAndCustom_DeductBaseAndChargeTheirOwnPrices_WithSnapshots()
    {
        var owner = Owner();
        var (attar, config) = await StockedAttarAsync(owner, "SALE");
        var pack = Option(config, "P12");
        var custom = Option(config, "ML");

        var sale = await SellAsync(owner,
            new SaleLine(attar, 2m, 100m, pack, config.GetProperty("configId").GetInt64()),
            new SaleLine(attar, 75m, 5m, custom, config.GetProperty("configId").GetInt64()));

        // 200 + 375 net, plus 10% VAT.
        Assert.Equal(632.5m, sale.GetProperty("totalAmount").GetDecimal());
        Assert.Equal(57.5m, sale.GetProperty("tax").GetDecimal());
        var items = sale.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(24m, items[0].GetProperty("quantity").GetDecimal());
        Assert.Equal(2m, items[0].GetProperty("enteredQuantity").GetDecimal());
        Assert.Equal(100m, items[0].GetProperty("enteredUnitPrice").GetDecimal());
        Assert.Equal(200m, items[0].GetProperty("subtotal").GetDecimal());
        Assert.Equal(75m, items[1].GetProperty("quantity").GetDecimal());
        Assert.Equal(375m, items[1].GetProperty("subtotal").GetDecimal());

        var saleId = sale.GetProperty("id").GetInt64();
        await _fixture.WithDbAsync(async db =>
        {
            Assert.Equal(401m, (await db.Inventories.AsNoTracking().SingleAsync(row => row.VariantId == attar && row.LocationId == UomPostgresFixture.Outlet)).Quantity);
            var rows = await db.StockLedgers.AsNoTracking().Where(row => row.VariantId == attar && row.ReferenceType == "sale" && row.ReferenceId == saleId).OrderBy(row => row.Id).ToListAsync();
            Assert.Equal([24m, 75m], rows.Select(row => row.QtyOut));
            Assert.Equal([476m, 401m], rows.Select(row => row.BalanceAfter));
            // COGS: 99 ml at ৳2/ml, unaffected by the selling prices.
            Assert.Equal(198m, await SnapshotCostAsync(db, saleId));
        });
    }

    [Fact]
    public async Task PosSale_RejectsWrongPrice_FractionOfPack_ExcessPrecision_Oversell_AndMissingOption()
    {
        var owner = Owner();
        var (attar, config) = await StockedAttarAsync(owner, "NEG");
        var pack = Option(config, "P12");
        var custom = Option(config, "ML");
        var version = config.GetProperty("configId").GetInt64();

        await AssertSaleFailsAsync(owner, new SaleLine(attar, 1m, 90m, pack, version), HttpStatusCode.Conflict, "PRICE");
        await AssertSaleFailsAsync(owner, new SaleLine(attar, 1.5m, 100m, pack, version), HttpStatusCode.BadRequest, UomErrorCode("PRECISION_EXCEEDED"));
        await AssertSaleFailsAsync(owner, new SaleLine(attar, 12.5678m, 5m, custom, version), HttpStatusCode.BadRequest, UomErrorCode("PRECISION_EXCEEDED"));
        await AssertSaleFailsAsync(owner, new SaleLine(attar, 50m, 100m, pack, version), HttpStatusCode.BadRequest, "Insufficient");
        await AssertSaleFailsAsync(owner, new SaleLine(attar, 1m, 100m, null, null), HttpStatusCode.BadRequest, UomErrorCode("OPTION_REQUIRED"));

        await _fixture.WithDbAsync(async db =>
            Assert.Equal(500m, (await db.Inventories.AsNoTracking().SingleAsync(row => row.VariantId == attar && row.LocationId == UomPostgresFixture.Outlet)).Quantity));
    }

    [Fact]
    public async Task ChangedConfiguration_RefusesStaleCheckout_AndCommittedSnapshotsStayAsSold()
    {
        var owner = Owner();
        var (attar, config) = await StockedAttarAsync(owner, "STALE");
        var oldVersion = config.GetProperty("configId").GetInt64();
        var sale = await SellAsync(owner, new SaleLine(attar, 1m, 100m, Option(config, "P12"), oldVersion));

        var updated = await SaveConfigAsync(owner, attar, "ml", Jar500(owner), [Pack("P12", 12m, 120m), CustomMl(6m)]);

        await AssertSaleFailsAsync(owner, new SaleLine(attar, 1m, 100m, Option(config, "P12"), oldVersion), HttpStatusCode.Conflict, UomErrorCode("CONFIG_CHANGED"));

        var fresh = await SellAsync(owner, new SaleLine(attar, 1m, 120m, Option(updated, "P12"), updated.GetProperty("configId").GetInt64()));
        Assert.Equal(132m, fresh.GetProperty("totalAmount").GetDecimal());

        var old = await Data(owner, $"/api/sales/{sale.GetProperty("id").GetInt64()}");
        Assert.Equal(110m, old.GetProperty("totalAmount").GetDecimal());
        Assert.Equal(100m, old.GetProperty("items")[0].GetProperty("enteredUnitPrice").GetDecimal());
    }

    [Fact]
    public async Task LegacyPcsSale_BehavesAsBefore_AndFractionsAreRefused()
    {
        var owner = Owner();
        var before = await OnHandAsync(UomPostgresFixture.LegacyVariant);
        var sale = await SellAsync(owner, new SaleLine(UomPostgresFixture.LegacyVariant, 2m, 50m, null, null));
        Assert.Equal(110m, sale.GetProperty("totalAmount").GetDecimal());
        var item = sale.GetProperty("items")[0];
        Assert.Equal(2, item.GetProperty("quantity").GetInt32());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("unitCode").ValueKind);
        Assert.Equal(before - 2m, await OnHandAsync(UomPostgresFixture.LegacyVariant));

        await AssertSaleFailsAsync(owner, new SaleLine(UomPostgresFixture.LegacyVariant, 1.5m, 50m, null, null), HttpStatusCode.BadRequest, "whole number");
    }

    [Fact]
    public async Task Packaging_IsConsumedOnce_CountsInCogs_AndShortageBlocksTheWholeSale()
    {
        var owner = Owner();
        var bottle = await _fixture.CreateVariantAsync("BTL");
        await ReceivePcsAsync(owner, bottle, 3m, 8m);
        var (attar, config) = await StockedAttarAsync(owner, "PKG", [Pack("P12", 12m, 100m, [new { componentVariantId = bottle, quantity = 1m, isRequired = true }]), CustomMl(5m, [new { componentVariantId = bottle, quantity = 1m, isRequired = false }])]);
        var pack = Option(config, "P12");
        var custom = Option(config, "ML");
        var version = config.GetProperty("configId").GetInt64();

        var sale = await SellAsync(owner, new SaleLine(attar, 2m, 100m, pack, version));
        Assert.Equal(1m, await OnHandAsync(bottle));
        await _fixture.WithDbAsync(async db =>
            // 24 ml at ৳2 plus 2 bottles at ৳8.
            Assert.Equal(64m, await SnapshotCostAsync(db, sale.GetProperty("id").GetInt64())));

        // A loose custom sale does not have to take a bottle.
        await SellAsync(owner, new SaleLine(attar, 10m, 5m, custom, version));
        Assert.Equal(1m, await OnHandAsync(bottle));

        await AssertSaleFailsAsync(owner, new SaleLine(attar, 2m, 100m, pack, version), HttpStatusCode.BadRequest, UomErrorCode("INSUFFICIENT_PACKAGING_STOCK"));
        Assert.Equal(1m, await OnHandAsync(bottle));
        Assert.Equal(466m, await OnHandAsync(attar));
    }

    [Fact]
    public async Task PackState_GrnSplit_OpenPreservesQuantityAndValue_SealedAndLooseEnforced()
    {
        var owner = Owner();
        var attar = await _fixture.CreateVariantAsync("PST");
        var jar = await UnitIdAsync(owner, "jar");
        var config = await SaveConfigAsync(owner, attar, "ml",
            [new { code = "JAR500", unitId = jar, label = "500 ml Jar", factorNumerator = 500m, factorDenominator = 1m, isDefault = true, isSealedPack = true }],
            [new { kind = "pack", code = "J500", label = "Sealed jar", unitId = (long?)null, baseQuantity = (decimal?)500m, price = 1500m, isDefault = false, sealedPackCode = (string?)"JAR500", isPreparedEligible = false, components = Array.Empty<object>() }, CustomMl(5m)],
            packTracking: true);
        await PurchaseAsync(owner, attar, config.GetProperty("purchaseOptions")[0].GetProperty("id").GetInt64(), 3m, 1000m);

        var state = await PackStateAsync(owner, attar);
        Assert.True(state.GetProperty("isReconciled").GetBoolean());
        Assert.Equal(1500m, state.GetProperty("onHand").GetDecimal());
        Assert.Equal(3, Balance(state, "sealed").GetProperty("packCount").GetInt32());

        var version = config.GetProperty("configId").GetInt64();
        // No loose stock yet, and a measured sale never opens a jar on its own.
        await AssertSaleFailsAsync(owner, new SaleLine(attar, 10m, 5m, Option(config, "ML"), version), HttpStatusCode.BadRequest, UomErrorCode("INSUFFICIENT_LOOSE_STOCK"));

        var openBody = new { variantId = attar, locationId = UomPostgresFixture.Outlet, locationType = "outlet", packCode = "JAR500", baseQtyPerPack = 500m, count = 1, idempotencyKey = Unique("open") };
        var valueBefore = await ValuationAsync(attar);
        var opened = await Send(owner, HttpMethod.Post, "/api/v1/uom/pack-state/open", openBody);
        Assert.True(opened.GetProperty("isReconciled").GetBoolean());
        Assert.Equal(1500m, opened.GetProperty("onHand").GetDecimal());
        Assert.Equal(2, Balance(opened, "sealed").GetProperty("packCount").GetInt32());
        Assert.Equal(500m, Balance(opened, "loose").GetProperty("baseQuantity").GetDecimal());
        Assert.Equal(valueBefore, await ValuationAsync(attar));

        // The same key does nothing a second time.
        var replay = await Send(owner, HttpMethod.Post, "/api/v1/uom/pack-state/open", openBody);
        Assert.Equal(2, Balance(replay, "sealed").GetProperty("packCount").GetInt32());

        await SellAsync(owner, new SaleLine(attar, 75m, 5m, Option(config, "ML"), version));
        await SellAsync(owner, new SaleLine(attar, 1m, 1500m, Option(config, "J500"), version));
        var after = await PackStateAsync(owner, attar);
        Assert.True(after.GetProperty("isReconciled").GetBoolean());
        Assert.Equal(925m, after.GetProperty("onHand").GetDecimal());
        Assert.Equal(1, Balance(after, "sealed").GetProperty("packCount").GetInt32());
        Assert.Equal(425m, Balance(after, "loose").GetProperty("baseQuantity").GetDecimal());
    }

    [Fact]
    public async Task Grn_SeparatesAcceptedFromDamaged_NothingDamagedIsStockedOrPayable()
    {
        var owner = Owner();
        var attar = await _fixture.CreateVariantAsync("GRN");
        var jar = await UnitIdAsync(owner, "jar");
        var config = await SaveConfigAsync(owner, attar, "ml",
            [new { code = "JAR500", unitId = jar, label = "500 ml Jar", factorNumerator = 500m, factorDenominator = 1m, isDefault = true, isSealedPack = true }],
            [CustomMl(5m)],
            packTracking: true);
        var optionId = config.GetProperty("purchaseOptions")[0].GetProperty("id").GetInt64();
        var po = await Send(owner, HttpMethod.Post, "/api/purchase-orders", new
        {
            supplierId = UomPostgresFixture.Supplier,
            locationType = "outlet",
            locationId = UomPostgresFixture.Outlet,
            orderDate = DateTime.UtcNow,
            status = "draft",
            items = new[] { new { variantId = attar, quantity = 3m, unitPrice = 1000m, discount = 0m, tax = 0m, purchaseOptionId = optionId } }
        });
        var poId = po.GetProperty("id").GetInt64();
        var poItemId = po.GetProperty("items")[0].GetProperty("id").GetInt64();
        await _fixture.WithDbAsync(db => db.PurchaseOrders.Where(row => row.Id == poId).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Status, "approved")));

        async Task<HttpResponseMessage> PostGrn(decimal received, int sealedPacks, decimal loose) =>
            await owner.PostAsJsonAsync("/api/grns", new
            {
                poId,
                receivedDate = DateTime.UtcNow,
                items = new[] { new { poItemId, receivedQty = received, damagedQty = 200m, rejectedQty = 0m, acceptedSealedPacks = sealedPacks, acceptedLooseQty = loose } }
            });

        using (var wrong = await PostGrn(1300m, 2, 200m))
            Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);

        using var created = await PostGrn(1300m, 2, 300m);
        var body = await created.Content.ReadAsStringAsync();
        Assert.True(created.IsSuccessStatusCode, body);
        using var json = JsonDocument.Parse(body);
        var grn = json.RootElement.GetProperty("data");
        Assert.Equal(200m, grn.GetProperty("items")[0].GetProperty("damagedQty").GetDecimal());
        var grnId = grn.GetProperty("id").GetInt64();
        await Send(owner, HttpMethod.Post, $"/api/grns/{grnId}/complete", new { });

        var state = await PackStateAsync(owner, attar);
        Assert.Equal(1300m, state.GetProperty("onHand").GetDecimal());
        Assert.Equal(2, Balance(state, "sealed").GetProperty("packCount").GetInt32());
        Assert.Equal(300m, Balance(state, "loose").GetProperty("baseQuantity").GetDecimal());
        await _fixture.WithDbAsync(async db =>
        {
            var ledger = await db.StockLedgers.AsNoTracking().SingleAsync(row => row.VariantId == attar);
            // 1300 ml at ৳2: the damaged 200 ml is not valued into stock.
            Assert.Equal(2600m, ledger.MovementValue);
        });
    }

    [Fact]
    public async Task Repack_ConservesValue_NeverDoubleConsumes_AndCostsThirtyTwoPerPack()
    {
        var owner = Owner();
        var bottle = await _fixture.CreateVariantAsync("RPB");
        await ReceivePcsAsync(owner, bottle, 30m, 8m);
        var (attar, config) = await RepackableAttarAsync(owner, "RPK", bottle);

        var valueBefore = await ValuationAsync(attar) + await ValuationAsync(bottle);
        var job = await CreateJobAsync(owner, attar, config, 20, Unique("job"));
        Assert.Equal("draft", job.GetProperty("status").GetString());
        var jobId = job.GetProperty("id").GetInt64();

        // Wastage has no approved route inside a repack.
        using (var wastage = await owner.PostAsJsonAsync($"/api/v1/uom/repacking/{jobId}/execute", new { wastageBaseQuantity = 5m }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, wastage.StatusCode);
            Assert.Contains(UomErrorCode("REPACK_WASTAGE_NOT_SUPPORTED"), await wastage.Content.ReadAsStringAsync());
        }

        var executed = await Send(owner, HttpMethod.Post, $"/api/v1/uom/repacking/{jobId}/execute", new { });
        Assert.Equal("executed", executed.GetProperty("status").GetString());
        Assert.Equal(480m, executed.GetProperty("contentValue").GetDecimal());
        Assert.Equal(160m, executed.GetProperty("packagingValue").GetDecimal());
        Assert.Equal(32m, executed.GetProperty("costPerPack").GetDecimal());

        // A retry changes nothing.
        await Send(owner, HttpMethod.Post, $"/api/v1/uom/repacking/{jobId}/execute", new { });
        Assert.Equal(10m, await OnHandAsync(bottle));
        Assert.Equal(500m, await OnHandAsync(attar));

        var state = await PackStateAsync(owner, attar);
        Assert.True(state.GetProperty("isReconciled").GetBoolean());
        Assert.Equal(20, Balance(state, "prepared").GetProperty("packCount").GetInt32());
        Assert.Equal(160m, Balance(state, "prepared").GetProperty("capitalizedValue").GetDecimal());
        Assert.Equal(260m, Balance(state, "loose").GetProperty("baseQuantity").GetDecimal());

        // Attar content + remaining bottles + capitalized bottles is the same total as before.
        var capitalized = Balance(state, "prepared").GetProperty("capitalizedValue").GetDecimal();
        Assert.Equal(valueBefore, await ValuationAsync(attar) + await ValuationAsync(bottle) + capitalized);

        // Selling a prepared pack takes no more bottles and no loose Attar.
        var version = config.GetProperty("configId").GetInt64();
        var sale = await SellAsync(owner, new SaleLine(attar, 2m, 100m, Option(config, "P12"), version, "prepared"));
        Assert.Equal(10m, await OnHandAsync(bottle));
        var afterSale = await PackStateAsync(owner, attar);
        Assert.Equal(18, Balance(afterSale, "prepared").GetProperty("packCount").GetInt32());
        Assert.Equal(144m, Balance(afterSale, "prepared").GetProperty("capitalizedValue").GetDecimal());
        Assert.Equal(260m, Balance(afterSale, "loose").GetProperty("baseQuantity").GetDecimal());
        await _fixture.WithDbAsync(async db =>
            // Two finished packs at ৳32 each.
            Assert.Equal(64m, await SnapshotCostAsync(db, sale.GetProperty("id").GetInt64())));

        // Some packs have left, so the job can no longer be undone as a whole.
        using var blocked = await owner.PostAsJsonAsync($"/api/v1/uom/repacking/{jobId}/reverse", new { });
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
    }

    [Fact]
    public async Task Repack_FailsAtomically_ReversesExactly_AndNeedsItsPermission()
    {
        var owner = Owner();
        var bottle = await _fixture.CreateVariantAsync("RBB");
        await ReceivePcsAsync(owner, bottle, 20m, 8m);
        var (attar, config) = await RepackableAttarAsync(owner, "RBK", bottle);

        // More packs than bottles: nothing moves.
        var tooMany = await CreateJobAsync(owner, attar, config, 21, Unique("many"));
        using (var short1 = await owner.PostAsJsonAsync($"/api/v1/uom/repacking/{tooMany.GetProperty("id").GetInt64()}/execute", new { }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, short1.StatusCode);
            Assert.Contains(UomErrorCode("INSUFFICIENT_PACKAGING_STOCK"), await short1.Content.ReadAsStringAsync());
        }

        Assert.Equal(20m, await OnHandAsync(bottle));
        Assert.Equal(500m, Balance(await PackStateAsync(owner, attar), "loose").GetProperty("baseQuantity").GetDecimal());

        var key = Unique("job");
        var job = await CreateJobAsync(owner, attar, config, 20, key);
        var duplicate = await CreateJobAsync(owner, attar, config, 20, key);
        Assert.Equal(job.GetProperty("id").GetInt64(), duplicate.GetProperty("id").GetInt64());

        var jobId = job.GetProperty("id").GetInt64();
        await Send(owner, HttpMethod.Post, $"/api/v1/uom/repacking/{jobId}/execute", new { });
        Assert.Equal(0m, await OnHandAsync(bottle));

        var reversed = await Send(owner, HttpMethod.Post, $"/api/v1/uom/repacking/{jobId}/reverse", new { });
        Assert.Equal("reversed", reversed.GetProperty("status").GetString());
        Assert.Equal(20m, await OnHandAsync(bottle));
        Assert.Equal(160m, await ValuationAsync(bottle));
        var state = await PackStateAsync(owner, attar);
        Assert.Equal(500m, Balance(state, "loose").GetProperty("baseQuantity").GetDecimal());
        Assert.True(state.GetProperty("isReconciled").GetBoolean());

        // Without products.view_cost the job is visible but its money is not.
        var noCost = _fixture.Client(UomPostgresFixture.OwnerId, "Manager", "inventory.repacking.view");
        var hidden = await Data(noCost, $"/api/v1/uom/repacking/{jobId}");
        Assert.Equal(JsonValueKind.Null, hidden.GetProperty("packagingValue").ValueKind);
        Assert.Equal(JsonValueKind.Null, hidden.GetProperty("costPerPack").ValueKind);

        var cashier = _fixture.Client(UomPostgresFixture.OwnerId, "Cashier", "sales.create");
        using var forbidden = await cashier.PostAsJsonAsync($"/api/v1/uom/repacking/{jobId}/execute", new { });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task Accounting_LedgerPlusCapitalizedValueStaysEqualToTheGeneralLedger()
    {
        var owner = Owner();
        var before = await ValuationReportAsync(owner);

        var bottle = await _fixture.CreateVariantAsync("GLB");
        await ReceivePcsAsync(owner, bottle, 20m, 8m);
        var (attar, config) = await RepackableAttarAsync(owner, "GLA", bottle);

        // Purchases: +160 and +1000 in both the ledger and the GL.
        var afterPurchase = await ValuationReportAsync(owner);
        Assert.Equal(before.Value + 1160m, afterPurchase.Value);
        Assert.Equal(before.Gl + 1160m, afterPurchase.Gl);

        // A repack moves ৳160 of bottles into prepared stock: no journal, and no change in total value.
        var job = await CreateJobAsync(owner, attar, config, 20, Unique("job"));
        await Send(owner, HttpMethod.Post, $"/api/v1/uom/repacking/{job.GetProperty("id").GetInt64()}/execute", new { });
        var afterRepack = await ValuationReportAsync(owner);
        Assert.Equal(afterPurchase.Value, afterRepack.Value);
        Assert.Equal(afterPurchase.Gl, afterRepack.Gl);
        Assert.Equal(before.Capitalized + 160m, afterRepack.Capitalized);

        // Selling 2 prepared packs takes 2 × ৳32 out of the ledger and, once posted, out of the GL.
        var sale = await SellAsync(owner, new SaleLine(attar, 2m, 100m, Option(config, "P12"), config.GetProperty("configId").GetInt64(), "prepared"));
        long postingId = 0;
        var saleId = sale.GetProperty("id").GetInt64();
        await _fixture.WithDbAsync(async db => postingId = await db.SaleFinancialPostings.AsNoTracking()
            .Where(row => row.SaleId == saleId && row.Kind == SaleFinancialPostingKinds.Sale)
            .Select(row => row.Id)
            .SingleAsync());
        await Send(owner, HttpMethod.Post, $"/api/financial-postings/sales/{postingId}/retry", new { });
        var afterSale = await ValuationReportAsync(owner);
        Assert.Equal(afterRepack.Value - 64m, afterSale.Value);
        Assert.Equal(afterRepack.Gl - 64m, afterSale.Gl);

        // Everything this test did leaves the reconciliation difference where it found it.
        Assert.Equal(before.Value - before.Gl, afterSale.Value - afterSale.Gl);
    }

    private static async Task<(decimal Value, decimal Gl, decimal Capitalized)> ValuationReportAsync(HttpClient client)
    {
        var report = await Data(client, "/api/accounting/reports/inventory-valuation");
        return (
            report.GetProperty("totalInventoryValue").GetDecimal(),
            report.GetProperty("generalLedgerBalance").GetDecimal(),
            report.GetProperty("capitalizedPackagingValue").GetDecimal());
    }

    [Fact]
    public async Task Refund_UsesTheOriginalSnapshot_RestoresOnlyWhatIsResellable_AndCostFollowsStock()
    {
        var owner = Owner();
        var (attar, config) = await StockedAttarAsync(owner, "RET");
        var version = config.GetProperty("configId").GetInt64();
        var sale = await SellAsync(owner, new SaleLine(attar, 1m, 100m, Option(config, "P12"), version));
        var saleId = sale.GetProperty("id").GetInt64();
        var itemId = sale.GetProperty("items")[0].GetProperty("id").GetInt64();

        // Prices change afterwards. The refund still follows what was sold.
        await SaveConfigAsync(owner, attar, "ml", Jar500(owner), [Pack("P12", 12m, 999m), CustomMl(50m)]);

        using var tooMuch = await owner.PostAsJsonAsync($"/api/sales/{saleId}/refund", new { items = new[] { new { variantId = attar, quantity = 13m, saleItemId = itemId } }, reason = "Too much" });
        Assert.Equal(HttpStatusCode.BadRequest, tooMuch.StatusCode);

        var refunded = await Send(owner, HttpMethod.Post, $"/api/sales/{saleId}/refund", new
        {
            items = new[] { new { variantId = attar, quantity = 7m, saleItemId = itemId, condition = "resellable" } },
            reason = "Partly used",
            cashRefundAmount = 58.33m
        });
        Assert.Equal("partially_refunded", refunded.GetProperty("status").GetString());
        Assert.Equal(495m, await OnHandAsync(attar));

        await _fixture.WithDbAsync(async db =>
        {
            var reversal = await db.SaleFinancialPostings.AsNoTracking().SingleAsync(row => row.SaleId == saleId && row.Kind == SaleFinancialPostingKinds.Reversal);
            using var snapshot = JsonDocument.Parse(reversal.SnapshotJson);
            // 7/12 of ৳100 net, and 7 ml at ৳2 back to inventory.
            Assert.Equal(58.33m, snapshot.RootElement.GetProperty("refundAmount").GetDecimal());
            Assert.Equal(14m, snapshot.RootElement.GetProperty("cost").GetDecimal());
            var line = await db.SaleReturnLines.AsNoTracking().SingleAsync(row => row.SaleItemId == itemId);
            Assert.Equal(7m, line.RestockedQuantity);
        });

        using var again = await owner.PostAsJsonAsync($"/api/sales/{saleId}/refund", new { items = new[] { new { variantId = attar, quantity = 1m, saleItemId = itemId } }, reason = "Again" });
        Assert.False(again.IsSuccessStatusCode);
    }

    [Fact]
    public async Task Refund_NonResellable_ReversesRevenueButLeavesStockAndCostAlone()
    {
        var owner = Owner();
        var (attar, config) = await StockedAttarAsync(owner, "DMG");
        var sale = await SellAsync(owner, new SaleLine(attar, 1m, 100m, Option(config, "P12"), config.GetProperty("configId").GetInt64()));
        var saleId = sale.GetProperty("id").GetInt64();
        var itemId = sale.GetProperty("items")[0].GetProperty("id").GetInt64();

        await Send(owner, HttpMethod.Post, $"/api/sales/{saleId}/refund", new
        {
            items = new[] { new { variantId = attar, quantity = 12m, saleItemId = itemId, condition = "non_resellable" } },
            reason = "Leaked"
        });

        Assert.Equal(488m, await OnHandAsync(attar));
        await _fixture.WithDbAsync(async db =>
        {
            var reversal = await db.SaleFinancialPostings.AsNoTracking().SingleAsync(row => row.SaleId == saleId && row.Kind == SaleFinancialPostingKinds.Reversal);
            using var snapshot = JsonDocument.Parse(reversal.SnapshotJson);
            Assert.Equal(100m, snapshot.RootElement.GetProperty("refundAmount").GetDecimal());
            Assert.Equal(0m, snapshot.RootElement.GetProperty("cost").GetDecimal());
            Assert.False(await db.StockLedgers.AnyAsync(row => row.VariantId == attar && row.TransactionType == StockLedgerTransactionType.Return));
        });
    }

    [Fact]
    public async Task Void_RestoresContentPackagingAndPreparedValue()
    {
        var owner = Owner();
        var bottle = await _fixture.CreateVariantAsync("VDB");
        await ReceivePcsAsync(owner, bottle, 22m, 8m);
        var (attar, config) = await RepackableAttarAsync(owner, "VDA", bottle);
        var job = await CreateJobAsync(owner, attar, config, 20, Unique("job"));
        await Send(owner, HttpMethod.Post, $"/api/v1/uom/repacking/{job.GetProperty("id").GetInt64()}/execute", new { });
        var version = config.GetProperty("configId").GetInt64();

        var prepared = await SellAsync(owner, new SaleLine(attar, 2m, 100m, Option(config, "P12"), version, "prepared"));
        var loose = await SellAsync(owner, new SaleLine(attar, 1m, 100m, Option(config, "P12"), version, "loose"));
        Assert.Equal(1m, await OnHandAsync(bottle));

        await Send(owner, HttpMethod.Post, $"/api/sales/{prepared.GetProperty("id").GetInt64()}/void", new { reason = "Mistake" });
        await Send(owner, HttpMethod.Post, $"/api/sales/{loose.GetProperty("id").GetInt64()}/void", new { reason = "Mistake" });

        Assert.Equal(2m, await OnHandAsync(bottle));
        Assert.Equal(500m, await OnHandAsync(attar));
        var state = await PackStateAsync(owner, attar);
        Assert.True(state.GetProperty("isReconciled").GetBoolean());
        Assert.Equal(20, Balance(state, "prepared").GetProperty("packCount").GetInt32());
        Assert.Equal(160m, Balance(state, "prepared").GetProperty("capitalizedValue").GetDecimal());
    }

    [Fact]
    public async Task Transfer_MovesSealedAndLooseAsDispatched_AndPreservesCost()
    {
        var owner = Owner();
        var attar = await _fixture.CreateVariantAsync("TRF");
        var jar = await UnitIdAsync(owner, "jar");
        var config = await SaveConfigAsync(owner, attar, "ml",
            [new { code = "JAR500", unitId = jar, label = "500 ml Jar", factorNumerator = 500m, factorDenominator = 1m, isDefault = true, isSealedPack = true }],
            [CustomMl(5m)],
            packTracking: true);
        await PurchaseAsync(owner, attar, config.GetProperty("purchaseOptions")[0].GetProperty("id").GetInt64(), 2m, 1000m);
        await Send(owner, HttpMethod.Post, "/api/v1/uom/pack-state/open", new { variantId = attar, locationId = UomPostgresFixture.Outlet, locationType = "outlet", packCode = "JAR500", baseQtyPerPack = 500m, count = 1, idempotencyKey = Unique("open") });

        var created = await Send(owner, HttpMethod.Post, "/api/stock-transfers", new
        {
            transferType = "direct",
            fromLocationId = UomPostgresFixture.Outlet,
            fromLocationType = "outlet",
            toLocationId = UomPostgresFixture.OutletTwo,
            toLocationType = "outlet",
            transferDate = DateTime.UtcNow,
            items = new[]
            {
                new
                {
                    variantId = attar,
                    enteredQuantity = 750m,
                    allocations = new object[]
                    {
                        new { state = "sealed", packCode = "JAR500", baseQtyPerPack = 500m, packCount = 1, baseQuantity = 500m },
                        new { state = "loose", packCode = (string?)null, baseQtyPerPack = 0m, packCount = 0, baseQuantity = 250m }
                    }
                }
            }
        });
        var transferId = created.GetProperty("id").GetInt64();
        await Send(owner, HttpMethod.Post, $"/api/stock-transfers/{transferId}/submit-and-dispatch", new { });

        var source = await PackStateAsync(owner, attar);
        Assert.Equal(250m, source.GetProperty("onHand").GetDecimal());
        Assert.True(source.GetProperty("isReconciled").GetBoolean());

        var receiver = _fixture.Client(UomPostgresFixture.OwnerId, "Manager", "*");
        receiver.DefaultRequestHeaders.Add("X-Test-OutletId", UomPostgresFixture.OutletTwo.ToString());
        await Send(receiver, HttpMethod.Post, $"/api/stock-transfers/{transferId}/receive", new { });
        var destination = await PackStateAsync(owner, attar, UomPostgresFixture.OutletTwo);
        Assert.Equal(750m, destination.GetProperty("onHand").GetDecimal());
        Assert.True(destination.GetProperty("isReconciled").GetBoolean());
        Assert.Equal(1, Balance(destination, "sealed").GetProperty("packCount").GetInt32());
        Assert.Equal(250m, Balance(destination, "loose").GetProperty("baseQuantity").GetDecimal());

        Assert.Equal(1500m, await ValuationAsync(attar, UomPostgresFixture.OutletTwo));
        Assert.Equal(500m, await ValuationAsync(attar));
    }

    [Fact]
    public async Task Transfer_PreparedPacks_SplitIntoWholePacks_AcceptedAndRejectedKeepTheirValue_NeverAFraction()
    {
        var owner = Owner();
        var bottle = await _fixture.CreateVariantAsync("TRB");
        await ReceivePcsAsync(owner, bottle, 20m, 8m);
        var (attar, config) = await RepackableAttarAsync(owner, "TRA", bottle);
        var job = await CreateJobAsync(owner, attar, config, 20, Unique("job"));
        await Send(owner, HttpMethod.Post, $"/api/v1/uom/repacking/{job.GetProperty("id").GetInt64()}/execute", new { });

        var created = await Send(owner, HttpMethod.Post, "/api/stock-transfers", new
        {
            transferType = "direct",
            fromLocationId = UomPostgresFixture.Outlet,
            fromLocationType = "outlet",
            toLocationId = UomPostgresFixture.OutletTwo,
            toLocationType = "outlet",
            transferDate = DateTime.UtcNow,
            items = new[]
            {
                new
                {
                    variantId = attar,
                    enteredQuantity = 60m,
                    allocations = new object[] { new { state = "prepared", packCode = "P12", baseQtyPerPack = 12m, packCount = 5, baseQuantity = 60m } }
                }
            }
        });
        var transferId = created.GetProperty("id").GetInt64();
        await Send(owner, HttpMethod.Post, $"/api/stock-transfers/{transferId}/submit-and-dispatch", new { });
        var source = await PackStateAsync(owner, attar);
        Assert.Equal(15, Balance(source, "prepared").GetProperty("packCount").GetInt32());
        Assert.Equal(120m, Balance(source, "prepared").GetProperty("capitalizedValue").GetDecimal());

        var receiver = _fixture.Client(UomPostgresFixture.OwnerId, "Manager", "*");
        receiver.DefaultRequestHeaders.Add("X-Test-OutletId", UomPostgresFixture.OutletTwo.ToString());
        // Half a pack is never received, and a prepared pack cannot go missing: its bottle value has nowhere to go.
        using (var fraction = await receiver.PostAsJsonAsync($"/api/stock-transfers/{transferId}/receive", new
        {
            items = new[] { new { variantId = attar, acceptedQuantity = 30m, rejectedQuantity = 30m, missingQuantity = 0m, damagedQuantity = 0m, remarks = "Partly" } }
        }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, fraction.StatusCode);
            Assert.Contains(UomErrorCode("QUANTITY_MISMATCH"), await fraction.Content.ReadAsStringAsync());
        }
        using (var missing = await receiver.PostAsJsonAsync($"/api/stock-transfers/{transferId}/receive", new
        {
            items = new[] { new { variantId = attar, acceptedQuantity = 48m, rejectedQuantity = 0m, missingQuantity = 12m, damagedQuantity = 0m, remarks = "One lost" } }
        }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
            Assert.Contains(UomErrorCode("UNSUPPORTED_OPERATION"), await missing.Content.ReadAsStringAsync());
        }

        // Two whole packs accepted, three rejected: ৳40 of packaging splits ৳16 here and ৳24 back to the source.
        await Send(receiver, HttpMethod.Post, $"/api/stock-transfers/{transferId}/receive", new
        {
            items = new[] { new { variantId = attar, acceptedQuantity = 24m, rejectedQuantity = 36m, missingQuantity = 0m, damagedQuantity = 0m, remarks = "Three wrong" } }
        });
        var destination = await PackStateAsync(owner, attar, UomPostgresFixture.OutletTwo);
        Assert.True(destination.GetProperty("isReconciled").GetBoolean());
        Assert.Equal(2, Balance(destination, "prepared").GetProperty("packCount").GetInt32());
        Assert.Equal(16m, Balance(destination, "prepared").GetProperty("capitalizedValue").GetDecimal());

        long returnId = 0;
        await _fixture.WithDbAsync(async db => returnId = await db.StockTransfers.AsNoTracking()
            .Where(row => row.OriginalTransferId == transferId).Select(row => row.Id).SingleAsync());
        await Send(owner, HttpMethod.Post, $"/api/stock-transfers/{returnId}/receive", new { });

        var after = await PackStateAsync(owner, attar);
        Assert.True(after.GetProperty("isReconciled").GetBoolean());
        Assert.Equal(18, Balance(after, "prepared").GetProperty("packCount").GetInt32());
        Assert.Equal(144m, Balance(after, "prepared").GetProperty("capitalizedValue").GetDecimal());
    }

    [Fact]
    public async Task EnablingTracking_ClassifiesStockAsLoose_AndSalesBeforeItReturnAsLoose()
    {
        var owner = Owner();
        var (attar, config) = await StockedAttarAsync(owner, "ENB");
        var sale = await SellAsync(owner, new SaleLine(attar, 1m, 100m, Option(config, "P12"), config.GetProperty("configId").GetInt64()));

        await SaveConfigAsync(owner, attar, "ml", Jar500(owner), [Pack("P12", 12m, 100m), CustomMl(5m)], packTracking: true);
        var state = await PackStateAsync(owner, attar);
        Assert.True(state.GetProperty("isReconciled").GetBoolean());
        Assert.Equal(488m, Balance(state, "loose").GetProperty("baseQuantity").GetDecimal());

        await Send(owner, HttpMethod.Post, $"/api/sales/{sale.GetProperty("id").GetInt64()}/void", new { reason = "Mistake" });
        var after = await PackStateAsync(owner, attar);
        Assert.True(after.GetProperty("isReconciled").GetBoolean());
        Assert.Equal(500m, Balance(after, "loose").GetProperty("baseQuantity").GetDecimal());
    }

    [Fact]
    public async Task PackagingItem_CannotTrackPackState_AndDeletingIsGuarded()
    {
        var owner = Owner();
        var bottle = await _fixture.CreateVariantAsync("DLB");
        var attar = await _fixture.CreateVariantAsync("DLA");
        await SaveConfigAsync(owner, attar, "ml", [], [Pack("P12", 12m, 100m, [new { componentVariantId = bottle, quantity = 1m, isRequired = true }])]);

        using var tracked = await PutConfigAsync(owner, bottle, "pcs", [], [], packTracking: true);
        Assert.Equal(HttpStatusCode.BadRequest, tracked.StatusCode);

        using var inUse = await owner.DeleteAsync($"/api/products/variants/{bottle}");
        Assert.False(inUse.IsSuccessStatusCode);
    }

    [Fact]
    public async Task Grn_StaysSealedWhenTheConfigurationChangedAfterThePurchaseOrder()
    {
        var owner = Owner();
        var attar = await _fixture.CreateVariantAsync("GVS");
        var jar = await UnitIdAsync(owner, "jar");
        object[] purchase = [new { code = "JAR500", unitId = jar, label = "500 ml Jar", factorNumerator = 500m, factorDenominator = 1m, isDefault = true, isSealedPack = true }];
        var config = await SaveConfigAsync(owner, attar, "ml", purchase, [CustomMl(5m)], packTracking: true);
        var po = await Send(owner, HttpMethod.Post, "/api/purchase-orders", new
        {
            supplierId = UomPostgresFixture.Supplier,
            locationType = "outlet",
            locationId = UomPostgresFixture.Outlet,
            orderDate = DateTime.UtcNow,
            status = "draft",
            items = new[] { new { variantId = attar, quantity = 2m, unitPrice = 1000m, discount = 0m, tax = 0m, purchaseOptionId = config.GetProperty("purchaseOptions")[0].GetProperty("id").GetInt64() } }
        });
        var poId = po.GetProperty("id").GetInt64();
        var poItemId = po.GetProperty("items")[0].GetProperty("id").GetInt64();
        await _fixture.WithDbAsync(db => db.PurchaseOrders.Where(row => row.Id == poId).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Status, "approved")));

        // A price edit creates new option rows; the order still points at the old one.
        await SaveConfigAsync(owner, attar, "ml", purchase, [CustomMl(6m)], packTracking: true);

        var grn = await Send(owner, HttpMethod.Post, "/api/grns", new { poId, receivedDate = DateTime.UtcNow, items = new[] { new { poItemId, receivedQty = 1000m, acceptedSealedPacks = 2, acceptedLooseQty = 0m } } });
        await Send(owner, HttpMethod.Post, $"/api/grns/{grn.GetProperty("id").GetInt64()}/complete", new { });

        var state = await PackStateAsync(owner, attar);
        Assert.Equal(2, Balance(state, "sealed").GetProperty("packCount").GetInt32());
        Assert.True(state.GetProperty("isReconciled").GetBoolean());
    }

    [Fact]
    public async Task TransferReceipt_RefusesAFractionOfAPlainPcsItem()
    {
        var owner = Owner();
        var shirt = await _fixture.CreateVariantAsync("TPC");
        await ReceivePcsAsync(owner, shirt, 5m, 10m);
        var created = await Send(owner, HttpMethod.Post, "/api/stock-transfers", new
        {
            transferType = "direct",
            fromLocationId = UomPostgresFixture.Outlet,
            fromLocationType = "outlet",
            toLocationId = UomPostgresFixture.OutletTwo,
            toLocationType = "outlet",
            transferDate = DateTime.UtcNow,
            items = new[] { new { variantId = shirt, quantity = 1m, transferQuantity = 1m } }
        });
        var transferId = created.GetProperty("id").GetInt64();
        await Send(owner, HttpMethod.Post, $"/api/stock-transfers/{transferId}/submit-and-dispatch", new { });

        var receiver = _fixture.Client(UomPostgresFixture.OwnerId, "Manager", "*");
        receiver.DefaultRequestHeaders.Add("X-Test-OutletId", UomPostgresFixture.OutletTwo.ToString());
        using var fractional = await receiver.PostAsJsonAsync($"/api/stock-transfers/{transferId}/receive", new
        {
            items = new[] { new { variantId = shirt, acceptedQuantity = 0.5m, rejectedQuantity = 0.5m, missingQuantity = 0m, damagedQuantity = 0m, remarks = "Half" } }
        });
        Assert.Equal(HttpStatusCode.BadRequest, fractional.StatusCode);
        Assert.Contains(UomErrorCode("PRECISION_EXCEEDED"), await fractional.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task RepackRead_FollowsLocationAccess()
    {
        var owner = Owner();
        var bottle = await _fixture.CreateVariantAsync("LCB");
        await ReceivePcsAsync(owner, bottle, 2m, 8m);
        var (attar, config) = await RepackableAttarAsync(owner, "LCA", bottle);
        var job = await CreateJobAsync(owner, attar, config, 1, Unique("job"));
        var jobId = job.GetProperty("id").GetInt64();

        var outsider = _fixture.Client(UomPostgresFixture.RestrictedUserId, "Viewer", "inventory.repacking.view,products.view_cost");
        using var read = await outsider.GetAsync($"/api/v1/uom/repacking/{jobId}");
        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
        var list = await Data(outsider, "/api/v1/uom/repacking");
        Assert.DoesNotContain(list.GetProperty("items").EnumerateArray(), row => row.GetProperty("id").GetInt64() == jobId);
    }

    [Fact]
    public async Task Fingerprint_ReusedKeyWithADifferentOption_IsAConflictNotAReplay()
    {
        var owner = Owner();
        var (attar, config) = await StockedAttarAsync(owner, "FPK", [Pack("A", 12m, 100m), Pack("B", 24m, 100m)]);
        var version = config.GetProperty("configId").GetInt64();
        var key = Unique("sale");
        object Body(string code) => new
        {
            outletId = UomPostgresFixture.Outlet,
            terminalId = UomPostgresFixture.Terminal,
            discount = 0m,
            tax = 10m,
            paymentMethod = "card",
            idempotencyKey = key,
            payments = new[] { new { method = "card", amount = 110m } },
            items = new[] { new { variantId = attar, quantity = 1m, unitPrice = 100m, discountAmount = 0m, sellingOptionId = (long?)Option(config, code), expectedConfigVersionId = (long?)version } }
        };
        await Send(owner, HttpMethod.Post, "/api/sales", Body("A"));
        using var second = await owner.PostAsJsonAsync("/api/sales", Body("B"));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(488m, await OnHandAsync(attar));
    }

    [Fact]
    public async Task Adjustment_ConvertsEnteredUnits_AndOverLooseIsRefused()
    {
        var owner = Owner();
        var (attar, config) = await StockedAttarAsync(owner, "ADJ");
        var optionId = config.GetProperty("purchaseOptions")[0].GetProperty("id").GetInt64();

        await Send(owner, HttpMethod.Post, "/api/stock-adjustments/batch", new
        {
            action = "SubmitAndApprove",
            locationId = UomPostgresFixture.Outlet,
            locationType = "outlet",
            reason = "Damaged",
            items = new[] { new { variantId = attar, enteredQuantity = -0.5m, unitOptionId = (long?)null, quantityChange = 0m } }
        });

        var quantity = await OnHandAsync(attar);
        Assert.Equal(499.5m, quantity);

        using var fractional = await owner.PostAsJsonAsync("/api/stock-adjustments/batch", new
        {
            action = "Draft",
            locationId = UomPostgresFixture.Outlet,
            locationType = "outlet",
            reason = "Damaged",
            items = new[] { new { variantId = attar, enteredQuantity = 1.5m, unitOptionId = (long?)optionId, quantityChange = 0m } }
        });
        // A jar is a whole-number unit.
        Assert.Equal(HttpStatusCode.BadRequest, fractional.StatusCode);
        Assert.Contains(UomErrorCode("PRECISION_EXCEEDED"), await fractional.Content.ReadAsStringAsync());

        using var tooMuch = await owner.PostAsJsonAsync("/api/stock-adjustments/batch", new
        {
            action = "SubmitAndApprove",
            locationId = UomPostgresFixture.Outlet,
            locationType = "outlet",
            reason = "Lost",
            items = new[] { new { variantId = attar, enteredQuantity = -600m, unitOptionId = (long?)null, quantityChange = 0m } }
        });
        Assert.False(tooMuch.IsSuccessStatusCode);
        Assert.Equal(499.5m, await OnHandAsync(attar));
    }

    [Fact]
    public async Task Count_MixedUnitsSumToBase_AndWrongSealedCountIsRefused()
    {
        var owner = Owner();
        var attar = await _fixture.CreateVariantAsync("CNT");
        var jar = await UnitIdAsync(owner, "jar");
        var config = await SaveConfigAsync(owner, attar, "ml",
            [new { code = "JAR500", unitId = jar, label = "500 ml Jar", factorNumerator = 500m, factorDenominator = 1m, isDefault = true, isSealedPack = true }],
            [CustomMl(5m)],
            packTracking: true);
        var option = config.GetProperty("purchaseOptions")[0].GetProperty("id").GetInt64();
        await PurchaseAsync(owner, attar, option, 3m, 1000m);
        await Send(owner, HttpMethod.Post, "/api/v1/uom/pack-state/open", new { variantId = attar, locationId = UomPostgresFixture.Outlet, locationType = "outlet", packCode = "JAR500", baseQtyPerPack = 500m, count = 1, idempotencyKey = Unique("open") });

        long countId = 0, lineId = 0;
        await _fixture.WithDbAsync(async db =>
        {
            var count = new StockCount
            {
                BusinessId = UomPostgresFixture.Business,
                StockCountNo = Unique("SC"),
                LocationId = UomPostgresFixture.Outlet,
                LocationType = "outlet",
                Status = StockCount.StatusDraft,
                CreatedBy = UomPostgresFixture.OwnerId,
                Lines =
                [
                    new StockCountLine { ProductId = await db.ProductVariants.Where(row => row.Id == attar).Select(row => row.ProductId).SingleAsync(), VariantId = attar, ProductName = "Attar", ProductCode = "A", VariantName = "Std", CurrentStock = 1500m }
                ]
            };
            db.StockCounts.Add(count);
            await db.SaveChangesAsync();
            countId = count.Id;
            lineId = count.Lines.Single().Id;
        });

        var path = $"/api/v1/uom/stock-counts/{countId}/lines/{lineId}/entries";
        var saved = await Send(owner, HttpMethod.Put, path, new object[]
        {
            new { unitOptionId = (long?)option, enteredQuantity = 2m, state = "sealed", packCode = "JAR500", baseQtyPerPack = 500m },
            new { unitOptionId = (long?)null, enteredQuantity = 175m, state = "loose", packCode = (string?)null, baseQtyPerPack = 0m }
        });
        Assert.Equal(1175m, saved.GetProperty("physicalStock").GetDecimal());
        Assert.Equal(-325m, saved.GetProperty("difference").GetDecimal());

        using var wrong = await owner.PutAsJsonAsync(path, new object[]
        {
            new { unitOptionId = (long?)option, enteredQuantity = 1m, state = "sealed", packCode = "JAR500", baseQtyPerPack = 500m }
        });
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Contains(UomErrorCode("PACK_STATE_MISMATCH"), await wrong.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Isolation_ForeignVariantAndLocation_AreUnreachable_AndPermissionsHold()
    {
        var owner = Owner();
        using var foreignConfig = await owner.GetAsync($"/api/v1/uom/variants/{UomPostgresFixture.ForeignVariant}/config");
        Assert.Equal(HttpStatusCode.NotFound, foreignConfig.StatusCode);
        using var foreignWrite = await PutConfigAsync(owner, UomPostgresFixture.ForeignVariant, "ml", [], [CustomMl(5m)]);
        Assert.Equal(HttpStatusCode.NotFound, foreignWrite.StatusCode);

        var foreignOwner = _fixture.Client(UomPostgresFixture.ForeignOwnerId, "BusinessOwner", "*", UomPostgresFixture.ForeignBusiness);
        var variant = await _fixture.CreateVariantAsync("ISO");
        using var other = await foreignOwner.GetAsync($"/api/v1/uom/variants/{variant}/config");
        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);

        var viewer = _fixture.Client(UomPostgresFixture.OwnerId, "Viewer", "reports.view");
        using var noConfig = await viewer.GetAsync($"/api/v1/uom/variants/{variant}/config");
        Assert.Equal(HttpStatusCode.Forbidden, noConfig.StatusCode);
        using var noCreate = await viewer.PostAsJsonAsync("/api/v1/uom/units", new { code = "x", name = "x", symbol = "x", dimension = "mass", quantityPrecision = 0, isWholeUnit = false });
        Assert.Equal(HttpStatusCode.Forbidden, noCreate.StatusCode);

        // A packaging item from another business cannot be linked.
        using var foreignPackaging = await PutConfigAsync(owner, variant, "ml", [], [Pack("P1", 1m, 1m, [new { componentVariantId = UomPostgresFixture.ForeignVariant, quantity = 1m, isRequired = true }])]);
        Assert.Equal(HttpStatusCode.BadRequest, foreignPackaging.StatusCode);
    }

    [Fact]
    public async Task Concurrent_RepackExecutions_ConsumeOnce()
    {
        var owner = Owner();
        var bottle = await _fixture.CreateVariantAsync("CCB");
        await ReceivePcsAsync(owner, bottle, 40m, 8m);
        var (attar, config) = await RepackableAttarAsync(owner, "CCA", bottle);
        var job = await CreateJobAsync(owner, attar, config, 20, Unique("job"));
        var path = $"/api/v1/uom/repacking/{job.GetProperty("id").GetInt64()}/execute";

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Owner().PostAsJsonAsync(path, new { })));
        Assert.Contains(results, response => response.IsSuccessStatusCode);
        foreach (var response in results)
            response.Dispose();

        Assert.Equal(20m, await OnHandAsync(bottle));
        var state = await PackStateAsync(owner, attar);
        Assert.Equal(20, Balance(state, "prepared").GetProperty("packCount").GetInt32());
        Assert.True(state.GetProperty("isReconciled").GetBoolean());
    }

    [Fact]
    public async Task Concurrent_Sales_NeverOversell()
    {
        var owner = Owner();
        var (attar, config) = await StockedAttarAsync(owner, "CCS");
        var pack = Option(config, "P12");
        var version = config.GetProperty("configId").GetInt64();

        // 500 ml covers 41 packs; 3 × 20 asks for more than that.
        var results = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => TrySellAsync(Owner(), new SaleLine(attar, 20m, 100m, pack, version))));
        var sold = results.Count(ok => ok);
        Assert.InRange(sold, 1, 2);
        Assert.Equal(500m - sold * 240m, await OnHandAsync(attar));
    }

    private sealed record SaleLine(long VariantId, decimal Quantity, decimal UnitPrice, long? OptionId, long? Version, string? State = null);

    private static string UomErrorCode(string suffix) => "UOM_" + suffix;

    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..Math.Min(prefix.Length + 12, 20)];

    private static object CustomMl(decimal price, object[]? components = null) =>
        new { kind = "custom", code = "ML", label = "Custom ml", unitId = (long?)UomPostgresFixture.MlUnitId, baseQuantity = (decimal?)null, price, isDefault = false, sealedPackCode = (string?)null, isPreparedEligible = false, components = components ?? [] };

    private static object Pack(string code, decimal baseQuantity, decimal price, object[]? components = null, bool prepared = false) =>
        new { kind = "pack", code, label = $"{baseQuantity} ml pack", unitId = (long?)null, baseQuantity = (decimal?)baseQuantity, price, isDefault = false, sealedPackCode = (string?)null, isPreparedEligible = prepared, components = components ?? [] };

    private object[] Jar500(HttpClient owner) =>
        [new { code = "JAR500", unitId = UnitIdAsync(owner, "jar").GetAwaiter().GetResult(), label = "500 ml Jar", factorNumerator = 500m, factorDenominator = 1m, isDefault = true, isSealedPack = false }];

    private async Task<(long Variant, JsonElement Config)> StockedAttarAsync(HttpClient owner, string code, object[]? selling = null)
    {
        var attar = await _fixture.CreateVariantAsync(code);
        var config = await SaveConfigAsync(owner, attar, "ml", Jar500(owner), selling ?? [Pack("P12", 12m, 100m), CustomMl(5m)]);
        await PurchaseAsync(owner, attar, config.GetProperty("purchaseOptions")[0].GetProperty("id").GetInt64(), 1m, 1000m);
        return (attar, config);
    }

    private async Task<(long Variant, JsonElement Config)> RepackableAttarAsync(HttpClient owner, string code, long bottle)
    {
        var attar = await _fixture.CreateVariantAsync(code);
        var config = await SaveConfigAsync(
            owner, attar, "ml", Jar500(owner),
            [Pack("P12", 12m, 100m, [new { componentVariantId = bottle, quantity = 1m, isRequired = true }], prepared: true), CustomMl(5m)],
            packTracking: true,
            repacking: true);
        await PurchaseAsync(owner, attar, config.GetProperty("purchaseOptions")[0].GetProperty("id").GetInt64(), 1m, 1000m);
        return (attar, config);
    }

    private static Task<JsonElement> CreateJobAsync(HttpClient owner, long attar, JsonElement config, int count, string key) =>
        Send(owner, HttpMethod.Post, "/api/v1/uom/repacking", new
        {
            sourceVariantId = attar,
            locationId = UomPostgresFixture.Outlet,
            locationType = "outlet",
            outputSellingOptionId = Option(config, "P12"),
            outputCount = count,
            idempotencyKey = key
        });

    private async Task<long> UnitIdAsync(HttpClient owner, string code)
    {
        var catalog = await Data(owner, "/api/v1/uom/units");
        return catalog.GetProperty("units").EnumerateArray().Single(unit => unit.GetProperty("code").GetString() == code).GetProperty("id").GetInt64();
    }

    private static long Option(JsonElement config, string code) =>
        config.GetProperty("sellingOptions").EnumerateArray().Single(option => option.GetProperty("code").GetString() == code).GetProperty("id").GetInt64();

    private static JsonElement Balance(JsonElement state, string kind) =>
        state.GetProperty("balances").EnumerateArray().Single(row => row.GetProperty("state").GetString() == kind);

    private async Task<HttpResponseMessage> PutConfigAsync(HttpClient client, long variant, string baseCode, object[] purchase, object[] selling, bool packTracking = false, bool repacking = false)
    {
        var baseUnitId = await UnitIdAsync(client, baseCode);
        return await client.PutAsJsonAsync($"/api/v1/uom/variants/{variant}/config", new
        {
            expectedVersion = (int?)null,
            config = new
            {
                baseUnitId,
                packStateTrackingEnabled = packTracking,
                repackingEnabled = repacking,
                purchaseOptions = purchase,
                sellingOptions = selling
            }
        });
    }

    private async Task<JsonElement> SaveConfigAsync(HttpClient client, long variant, string baseCode, object[] purchase, object[] selling, bool packTracking = false, bool repacking = false)
    {
        using var response = await PutConfigAsync(client, variant, baseCode, purchase, selling, packTracking, repacking);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"config: {response.StatusCode} {body}");
        using var json = JsonDocument.Parse(body);
        return json.RootElement.GetProperty("data").GetProperty("current").Clone();
    }

    private static Task<JsonElement> PurchaseAsync(HttpClient client, long variant, long optionId, decimal quantity, decimal unitPrice) =>
        Send(client, HttpMethod.Post, "/api/purchase-orders/purchase-receive", new
        {
            supplierId = UomPostgresFixture.Supplier,
            locationType = "outlet",
            locationId = UomPostgresFixture.Outlet,
            orderDate = DateTime.UtcNow,
            idempotencyKey = Unique("purchase"),
            items = new[] { new { variantId = variant, quantity, unitPrice, discount = 0m, tax = 0m, purchaseOptionId = optionId } }
        });

    private static Task<JsonElement> ReceivePcsAsync(HttpClient client, long variant, decimal quantity, decimal unitPrice) =>
        Send(client, HttpMethod.Post, "/api/purchase-orders/purchase-receive", new
        {
            supplierId = UomPostgresFixture.Supplier,
            locationType = "outlet",
            locationId = UomPostgresFixture.Outlet,
            orderDate = DateTime.UtcNow,
            idempotencyKey = Unique("purchase"),
            items = new[] { new { variantId = variant, quantity, unitPrice, discount = 0m, tax = 0m } }
        });

    private static object SaleBody(SaleLine[] lines)
    {
        // The business charges the default 10% VAT on top, which the server recomputes and checks.
        var net = lines.Sum(line => line.Quantity * line.UnitPrice);
        var tax = Math.Round(net * 0.10m, 2, MidpointRounding.AwayFromZero);
        var total = net + tax;
        return new
        {
            outletId = UomPostgresFixture.Outlet,
            terminalId = UomPostgresFixture.Terminal,
            discount = 0m,
            tax,
            paymentMethod = "card",
            idempotencyKey = Unique("sale"),
            payments = new[] { new { method = "card", amount = total } },
            items = lines.Select(line => new
            {
                variantId = line.VariantId,
                quantity = line.Quantity,
                unitPrice = line.UnitPrice,
                discountAmount = 0m,
                sellingOptionId = line.OptionId,
                expectedConfigVersionId = line.Version,
                fulfilmentState = line.State
            }).ToArray()
        };
    }

    private static Task<JsonElement> SellAsync(HttpClient client, params SaleLine[] lines) =>
        Send(client, HttpMethod.Post, "/api/sales", SaleBody(lines));

    private static async Task<bool> TrySellAsync(HttpClient client, params SaleLine[] lines)
    {
        using var response = await client.PostAsJsonAsync("/api/sales", SaleBody(lines));
        return response.IsSuccessStatusCode;
    }

    private static async Task AssertSaleFailsAsync(HttpClient client, SaleLine line, HttpStatusCode status, string fragment)
    {
        using var response = await client.PostAsJsonAsync("/api/sales", SaleBody([line]));
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(status == response.StatusCode, $"{response.StatusCode} {body}");
        Assert.Contains(fragment, body);
    }

    private Task<JsonElement> PackStateAsync(HttpClient client, long variant, long location = UomPostgresFixture.Outlet) =>
        Data(client, $"/api/v1/uom/pack-state?variantId={variant}&locationId={location}&locationType=outlet");

    private async Task<decimal> OnHandAsync(long variant)
    {
        decimal quantity = 0m;
        await _fixture.WithDbAsync(async db => quantity = await db.Inventories.AsNoTracking()
            .Where(row => row.VariantId == variant && row.LocationId == UomPostgresFixture.Outlet)
            .Select(row => row.Quantity)
            .SingleAsync());
        return quantity;
    }

    /// <summary>The weighted-average value the approved replay gives this variant at one location.</summary>
    private async Task<decimal> ValuationAsync(long variant, long location = UomPostgresFixture.Outlet)
    {
        decimal value = 0m;
        await _fixture.WithDbAsync(async db =>
        {
            var pool = new RetailPOS.Application.Inventory.Costing.WeightedAverageCostPool();
            var rows = await db.StockLedgers.AsNoTracking()
                .Where(row => row.VariantId == variant && row.LocationId == location)
                .OrderBy(row => row.BusinessDate).ThenBy(row => row.MovementSequence).ThenBy(row => row.Id)
                .ToListAsync();
            foreach (var row in rows)
                pool.Apply(row.QtyIn, row.QtyOut, row.MovementValue, row.UnitCost);
            value = pool.Value;
        });
        return value;
    }

    /// <summary>Every debit ever posted to the business's one Inventory account.</summary>
    private async Task<decimal> InventoryDebitsAsync()
    {
        decimal debit = 0m;
        await _fixture.WithDbAsync(async db =>
        {
            var account = await db.AccountingConfigurations.AsNoTracking().Where(row => row.BusinessId == UomPostgresFixture.Business).Select(row => row.InventoryAccountId).SingleAsync();
            debit = await db.JournalEntries.AsNoTracking()
                .Where(journal => journal.BusinessId == UomPostgresFixture.Business)
                .SelectMany(journal => journal.Lines)
                .Where(row => row.AccountId == account)
                .SumAsync(row => row.DebitAmount);
        });
        return debit;
    }

    private static async Task<decimal> SnapshotCostAsync(RetailPOSDbContext db, long saleId)
    {
        var posting = await db.SaleFinancialPostings.AsNoTracking().SingleAsync(row => row.SaleId == saleId && row.Kind == SaleFinancialPostingKinds.Sale);
        using var snapshot = JsonDocument.Parse(posting.SnapshotJson);
        return snapshot.RootElement.GetProperty("cost").GetDecimal();
    }

    private static async Task<JsonElement> Data(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{path}: {response.StatusCode} {body}");
        using var json = JsonDocument.Parse(body);
        return json.RootElement.GetProperty("data").Clone();
    }

    private static async Task<JsonElement> Send(HttpClient client, HttpMethod method, string path, object body, bool allowFailure = false)
    {
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body, body.GetType()) };
        using var response = await client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(allowFailure || response.IsSuccessStatusCode, $"{method} {path}: {response.StatusCode} {text}");
        using var json = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
        return json.RootElement.TryGetProperty("data", out var data) ? data.Clone() : default;
    }
}

public sealed class UomPostgresFixture : IAsyncLifetime
{
    public const long Business = 9101;
    public const long ForeignBusiness = 9201;
    public const long Outlet = 9111;
    public const long OutletTwo = 9112;
    public const long Warehouse = 9113;
    public const long ForeignOutlet = 9211;
    public const long OwnerId = 9120;
    public const long ForeignOwnerId = 9220;
    public const long RestrictedUserId = 9221;
    public const long Terminal = 9131;
    public const long TerminalTwo = 9132;
    public const long Supplier = 9141;
    public const long Category = 9151;
    public const long LegacyVariant = 9401;
    public const long ForeignVariant = 9402;

    /// <summary>Set once the migration's seed is read.</summary>
    public static long MlUnitId { get; private set; }

    private static long _nextId = 9500;
    private PostgresTestDatabase? _database;
    private MobileSalesDashboardPostgresApiFactory? _factory;

    public async Task InitializeAsync()
    {
        var builder = new NpgsqlConnectionStringBuilder(InfrastructureTestSettings.RequireIsolatedPostgres()) { Pooling = false };
        _database = await PostgresTestDatabase.CreateAsync(builder.ConnectionString, PostgresTestDatabasePrefix.Uom);
        try
        {
            _factory = new MobileSalesDashboardPostgresApiFactory(_database.ConnectionString);
            await using var scope = _factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            await db.Database.MigrateAsync();
            await SeedAsync(db);
            await scope.ServiceProvider.GetRequiredService<AccountingConfigurationProvisioner>().EnsureInitializedAsync(Business, OwnerId, CancellationToken.None);
            MlUnitId = await db.UomUnits.AsNoTracking().Where(unit => unit.Code == "ml" && unit.BusinessId == null).Select(unit => unit.Id).SingleAsync();
        }
        catch
        {
            if (_factory is not null)
                await _factory.DisposeAsync();
            await _database.DisposeAsync();
            _database = null;
            throw;
        }
    }

    private static async Task SeedAsync(RetailPOSDbContext db)
    {
        db.Businesses.AddRange(new Business { Id = Business, Name = "UOM business" }, new Business { Id = ForeignBusiness, Name = "Other" });
        db.Outlets.AddRange(
            new Outlet { Id = Outlet, BusinessId = Business, Name = "Outlet", InvoicePrefix = "UOM" },
            new Outlet { Id = OutletTwo, BusinessId = Business, Name = "Outlet two" },
            new Outlet { Id = ForeignOutlet, BusinessId = ForeignBusiness, Name = "Foreign" });
        db.Warehouses.Add(new Warehouse { Id = Warehouse, BusinessId = Business, Name = "Warehouse" });
        db.Users.AddRange(
            new User { Id = OwnerId, BusinessId = Business, InventoryLocationAccessScope = User.InventoryAccessAll, Name = "Owner", Email = "uom-owner@example.test", PasswordHash = "test-only", IsActive = true },
            new User { Id = RestrictedUserId, BusinessId = Business, InventoryLocationAccessScope = User.InventoryAccessAssignedOnly, Name = "Restricted", Email = "uom-restricted@example.test", PasswordHash = "test-only", IsActive = true },
            new User { Id = ForeignOwnerId, BusinessId = ForeignBusiness, InventoryLocationAccessScope = User.InventoryAccessAll, Name = "Foreign", Email = "uom-foreign@example.test", PasswordHash = "test-only", IsActive = true });
        db.UserOutletAssignments.AddRange(
            new UserOutletAssignment { UserId = OwnerId, OutletId = Outlet, BusinessId = Business, IsPrimary = true, IsActive = true },
            new UserOutletAssignment { UserId = OwnerId, OutletId = OutletTwo, BusinessId = Business, IsPrimary = false, IsActive = true });
        db.PosTerminals.Add(new PosTerminal { Id = Terminal, OutletId = Outlet, Name = "T1", Code = "T1", IsActive = true, IsDefault = true });
        db.Suppliers.Add(new Supplier { Id = Supplier, BusinessId = Business, Name = "Supplier" });
        db.Categories.AddRange(
            new Category { Id = Category, BusinessId = Business, Name = "Attar", Description = "" },
            new Category { Id = Category + 1, BusinessId = ForeignBusiness, Name = "Foreign", Description = "" });
        db.Products.AddRange(
            new Product { Id = LegacyVariant, BusinessId = Business, Name = "Shirt", ProductCode = "SHIRT", CategoryId = Category, BasePrice = 50m, CostPrice = 20m, Status = ProductStatus.Active },
            new Product { Id = ForeignVariant, BusinessId = ForeignBusiness, Name = "Foreign", ProductCode = "FOREIGN", CategoryId = Category + 1, Status = ProductStatus.Active });
        db.ProductVariants.AddRange(
            new ProductVariant { Id = LegacyVariant, BusinessId = Business, ProductId = LegacyVariant, Name = "Std", Sku = "SHIRT-1" },
            new ProductVariant { Id = ForeignVariant, BusinessId = ForeignBusiness, ProductId = ForeignVariant, Name = "Std", Sku = "FOREIGN-1" });
        db.Inventories.Add(new RetailPOS.Core.Entities.Inventory { VariantId = LegacyVariant, LocationId = Outlet, LocationType = "outlet", Quantity = 40 });
        db.CashSessions.Add(new CashSession
        {
            BusinessId = Business,
            OutletId = Outlet,
            TerminalId = Terminal,
            BusinessDate = DateTime.UtcNow.Date,
            OpenedAt = DateTime.UtcNow.AddHours(-1),
            OpeningCashAmount = 5000m,
            Status = "open",
            OpenedByUserId = OwnerId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    public async Task<long> CreateVariantAsync(string code)
    {
        var id = Interlocked.Increment(ref _nextId);
        await WithDbAsync(async db =>
        {
            db.Products.Add(new Product { Id = id, BusinessId = Business, Name = $"Item {code} {id}", ProductCode = $"{code}-{id}", CategoryId = Category, BasePrice = 10m, Status = ProductStatus.Active });
            db.ProductVariants.Add(new ProductVariant { Id = id, BusinessId = Business, ProductId = id, Name = "Standard", Sku = $"{code}-{id}-STD" });
            await db.SaveChangesAsync();
        });
        return id;
    }

    public HttpClient Client(long userId, string role, string permissions, long businessId = Business)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", businessId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-UserId", userId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        client.DefaultRequestHeaders.Add("X-Test-EffectiveRole", role);
        client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
        return client;
    }

    public async Task WithDbAsync(Func<RetailPOSDbContext, Task> action)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>());
    }

    private MobileSalesDashboardPostgresApiFactory Factory =>
        _factory ?? throw new InvalidOperationException("Fixture is not initialized.");

    public async Task DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();
        if (_database is not null)
            await _database.DisposeAsync();
    }
}
