using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;

namespace RetailPOS.Tests.Uom;

/// <summary>Closed gaps 4.1, 4.2 and 4.4: whole-pack partial receipt, opening prepared packs, rejected-only GRN lines.</summary>
public sealed partial class UomPostgresHttpTests
{
    private async Task<(long Variant, JsonElement Config)> SealedJarsAsync(HttpClient owner, string code, decimal jars)
    {
        var attar = await _fixture.CreateVariantAsync(code);
        var jar = await UnitIdAsync(owner, "jar");
        var config = await SaveConfigAsync(owner, attar, "ml",
            [new { code = "JAR500", unitId = jar, label = "500 ml Jar", factorNumerator = 500m, factorDenominator = 1m, isDefault = true, isSealedPack = true }],
            [new { kind = "pack", code = "J500", label = "Sealed jar", unitId = (long?)null, baseQuantity = (decimal?)500m, price = 1500m, isDefault = false, sealedPackCode = (string?)"JAR500", isPreparedEligible = false, components = Array.Empty<object>() }, CustomMl(5m)],
            packTracking: true);
        await PurchaseAsync(owner, attar, config.GetProperty("purchaseOptions")[0].GetProperty("id").GetInt64(), jars, 1000m);
        return (attar, config);
    }

    [Fact]
    public async Task Transfer_TenSealedJarsDispatched_EightReceived_TwoMissingStayADiscrepancy()
    {
        var owner = Owner();
        var (attar, _) = await SealedJarsAsync(owner, "TSJ", 10m);
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
                    enteredQuantity = 5000m,
                    allocations = new object[] { new { state = "sealed", packCode = "JAR500", baseQtyPerPack = 500m, packCount = 10, baseQuantity = 5000m } }
                }
            }
        });
        var transferId = created.GetProperty("id").GetInt64();
        await Send(owner, HttpMethod.Post, $"/api/stock-transfers/{transferId}/submit-and-dispatch", new { });

        var receiver = _fixture.Client(UomPostgresFixture.OwnerId, "Manager", "*");
        receiver.DefaultRequestHeaders.Add("X-Test-OutletId", UomPostgresFixture.OutletTwo.ToString());
        using (var split = await receiver.PostAsJsonAsync($"/api/stock-transfers/{transferId}/receive", new
        {
            items = new[] { new { variantId = attar, acceptedQuantity = 4250m, rejectedQuantity = 0m, missingQuantity = 750m, damagedQuantity = 0m, remarks = "Jar opened in transit" } }
        }))
            Assert.Equal(HttpStatusCode.BadRequest, split.StatusCode);

        await Send(receiver, HttpMethod.Post, $"/api/stock-transfers/{transferId}/receive", new
        {
            items = new[] { new { variantId = attar, acceptedQuantity = 4000m, rejectedQuantity = 0m, missingQuantity = 1000m, damagedQuantity = 0m, remarks = "Two jars short" } }
        });

        var destination = await PackStateAsync(owner, attar, UomPostgresFixture.OutletTwo);
        Assert.True(destination.GetProperty("isReconciled").GetBoolean());
        Assert.Equal(8, Balance(destination, "sealed").GetProperty("packCount").GetInt32());
        Assert.Equal(4000m, destination.GetProperty("onHand").GetDecimal());
        await _fixture.WithDbAsync(async db =>
        {
            var item = await db.StockTransferItems.AsNoTracking().SingleAsync(row => row.TransferId == transferId);
            Assert.Equal(1000m, item.MissingQuantity);
            Assert.True(await db.StockLedgers.AnyAsync(row => row.ReferenceId == transferId
                && row.TransactionType == StockLedgerTransactionType.TransferDiscrepancy));
        });
    }

    [Fact]
    public async Task OpenPrepared_OnePack_ContentGoesLoose_BottleReturnsWithItsValue_NothingCreatedOrLost()
    {
        var owner = Owner();
        var bottle = await _fixture.CreateVariantAsync("OPB");
        await ReceivePcsAsync(owner, bottle, 20m, 8m);
        var (attar, config) = await RepackableAttarAsync(owner, "OPA", bottle);
        var job = await CreateJobAsync(owner, attar, config, 20, Unique("job"));
        await Send(owner, HttpMethod.Post, $"/api/v1/uom/repacking/{job.GetProperty("id").GetInt64()}/execute", new { });
        var before = await ValuationReportAsync(owner);
        var attarValue = await ValuationAsync(attar);
        var bottleValue = await ValuationAsync(bottle);

        var body = new { variantId = attar, locationId = UomPostgresFixture.Outlet, locationType = "outlet", packCode = "P12", baseQtyPerPack = 12m, count = 1, packagingReusable = true, idempotencyKey = Unique("op") };
        var opened = await Send(owner, HttpMethod.Post, "/api/v1/uom/pack-state/open-prepared", body);
        await Send(owner, HttpMethod.Post, "/api/v1/uom/pack-state/open-prepared", body);

        Assert.True(opened.GetProperty("isReconciled").GetBoolean());
        Assert.Equal(19, Balance(opened, "prepared").GetProperty("packCount").GetInt32());
        Assert.Equal(152m, Balance(opened, "prepared").GetProperty("capitalizedValue").GetDecimal());
        Assert.Equal(500m, opened.GetProperty("onHand").GetDecimal());
        // The 12 ml never left the content pool; one bottle and its ৳8 come back to packaging stock.
        Assert.Equal(attarValue, await ValuationAsync(attar));
        Assert.Equal(bottleValue + 8m, await ValuationAsync(bottle));
        var after = await ValuationReportAsync(owner);
        Assert.Equal(before.Value, after.Value);
        Assert.Equal(before.Gl, after.Gl);

        using var broken = await owner.PostAsJsonAsync("/api/v1/uom/pack-state/open-prepared", body with { packagingReusable = false, idempotencyKey = Unique("op") });
        Assert.Equal(HttpStatusCode.BadRequest, broken.StatusCode);
        Assert.Contains(UomErrorCode("UNSUPPORTED_OPERATION"), await broken.Content.ReadAsStringAsync());

        using var cashier = _fixture.Client(UomPostgresFixture.OwnerId, "Cashier", "sales.create");
        using var forbidden = await cashier.PostAsJsonAsync("/api/v1/uom/pack-state/open-prepared", body with { idempotencyKey = Unique("op") });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task Grn_ALineWithNothingAccepted_RecordsTheRejection_ButPostsNoStockOrValue()
    {
        var owner = Owner();
        var attar = await _fixture.CreateVariantAsync("GRJ");
        var other = await _fixture.CreateVariantAsync("GRK");
        var po = await Send(owner, HttpMethod.Post, "/api/purchase-orders", new
        {
            supplierId = UomPostgresFixture.Supplier,
            locationType = "outlet",
            locationId = UomPostgresFixture.Outlet,
            orderDate = DateTime.UtcNow,
            status = "draft",
            items = new object[]
            {
                new { variantId = attar, quantity = 5m, unitPrice = 10m, discount = 0m, tax = 0m },
                new { variantId = other, quantity = 5m, unitPrice = 10m, discount = 0m, tax = 0m }
            }
        });
        var poId = po.GetProperty("id").GetInt64();
        await _fixture.WithDbAsync(db => db.PurchaseOrders.Where(row => row.Id == poId).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Status, "approved")));
        var lines = po.GetProperty("items").EnumerateArray().ToArray();
        long Line(long variant) => lines.Single(row => row.GetProperty("variantId").GetInt64() == variant).GetProperty("id").GetInt64();

        using (var nothing = await owner.PostAsJsonAsync("/api/grns", new
        {
            poId,
            receivedDate = DateTime.UtcNow,
            items = new[] { new { poItemId = Line(attar), receivedQty = 0m, damagedQty = 0m, rejectedQty = 5m } }
        }))
            Assert.Equal(HttpStatusCode.BadRequest, nothing.StatusCode);

        var grn = await Send(owner, HttpMethod.Post, "/api/grns", new
        {
            poId,
            receivedDate = DateTime.UtcNow,
            items = new object[]
            {
                new { poItemId = Line(attar), receivedQty = 0m, damagedQty = 0m, rejectedQty = 5m },
                new { poItemId = Line(other), receivedQty = 5m, damagedQty = 0m, rejectedQty = 0m }
            }
        });
        await Send(owner, HttpMethod.Post, $"/api/grns/{grn.GetProperty("id").GetInt64()}/complete", new { });

        await _fixture.WithDbAsync(async db =>
        {
            Assert.False(await db.Inventories.AnyAsync(row => row.VariantId == attar && row.Quantity != 0m));
            Assert.False(await db.StockLedgers.AnyAsync(row => row.VariantId == attar));
            var rejected = await db.GrnItems.AsNoTracking().SingleAsync(row => row.PurchaseOrderItem.VariantId == attar);
            Assert.Equal(0m, rejected.ReceivedQty);
            Assert.Equal(5m, rejected.RejectedQuantity);

            // A receipt that brought nothing carries no cost: the next sale falls back as if there were no receipt, never to 0.
            await db.Products.Where(row => row.Id == attar).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.CostPrice, 7m));
            var store = new RetailPOS.Infrastructure.Sales.SaleCreationStore(db, null!);
            Assert.Equal(7m, await store.ResolveHistoricalUnitCostSnapshotAsync(attar, CancellationToken.None));
        });
    }
}
