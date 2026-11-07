using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace RetailPOS.Tests.Uom;

/// <summary>
/// Inventory Intelligence shows stock that left its source and has not arrived (transfers In Transit) beside on-hand
/// stock, per unit, and never adds it twice. Other tests in this class leave transfers too, so every check is a change
/// measured around this test's own steps.
/// </summary>
public sealed partial class UomPostgresHttpTests
{
    private const string Dashboard = "/api/v1/owner-dashboard/inventory";
    private const string Period = "from=2026-01-01&to=2026-01-31";

    private sealed record DashboardTotals(
        Dictionary<string, decimal> OnHand,
        Dictionary<string, decimal> InTransit,
        Dictionary<string, decimal> Accounted,
        decimal OnHandValue,
        decimal InTransitValue,
        decimal AccountedValue,
        int InTransitLines);

    private static Dictionary<string, decimal> Units(JsonElement list) =>
        list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().ToDictionary(row => row.GetProperty("unitCode").GetString()!, row => row.GetProperty("quantity").GetDecimal())
            : [];

    private static decimal Subtotal(JsonElement coverage) =>
        coverage.GetProperty("valuedSubtotal").ValueKind == JsonValueKind.Number ? coverage.GetProperty("valuedSubtotal").GetDecimal() : 0m;

    private static async Task<DashboardTotals> DashboardAsync(HttpClient client, string location = "")
    {
        var summary = await Data(client, $"{Dashboard}/summary?{Period}{location}");
        var incoming = summary.GetProperty("inTransit").GetProperty("incoming");
        return new DashboardTotals(
            Units(summary.GetProperty("quantityByUnit")),
            Units(incoming.GetProperty("quantityByUnit")),
            Units(summary.GetProperty("totalAccountedByUnit")),
            Subtotal(summary.GetProperty("valuation")),
            Subtotal(incoming.GetProperty("valuation")),
            Subtotal(summary.GetProperty("totalAccountedValuation")),
            incoming.GetProperty("lines").GetInt32());
    }

    private static decimal Change(Dictionary<string, decimal> after, Dictionary<string, decimal> before, string unit) =>
        after.GetValueOrDefault(unit) - before.GetValueOrDefault(unit);

    private static string At(long location) => $"&locationId={location}&locationType=outlet";

    private HttpClient OutletTwoReceiver()
    {
        var receiver = _fixture.Client(UomPostgresFixture.OwnerId, "Manager", "*");
        receiver.DefaultRequestHeaders.Add("X-Test-OutletId", UomPostgresFixture.OutletTwo.ToString());
        return receiver;
    }

    /// <summary>500 m of fabric at ৳10 a metre, bought at the first outlet.</summary>
    private async Task<long> StockedFabricAsync(HttpClient owner, string code)
    {
        var fabric = await _fixture.CreateVariantAsync(code);
        var metre = await UnitIdAsync(owner, "m");
        var config = await SaveConfigAsync(owner, fabric, "m",
            [new { code = "M1", unitId = metre, label = "1 m", factorNumerator = 1m, factorDenominator = 1m, isDefault = true, isSealedPack = false }],
            [new { kind = "custom", code = "M", label = "per m", unitId = (long?)metre, baseQuantity = (decimal?)null, price = 20m, isDefault = false, sealedPackCode = (string?)null, isPreparedEligible = false, components = Array.Empty<object>() }]);
        await PurchaseAsync(owner, fabric, config.GetProperty("purchaseOptions")[0].GetProperty("id").GetInt64(), 500m, 10m);
        return fabric;
    }

    private static async Task<long> CreateTransferAsync(HttpClient owner, long from, long to, params (long Variant, decimal Quantity)[] lines)
    {
        var created = await Send(owner, HttpMethod.Post, "/api/stock-transfers", new
        {
            transferType = "direct",
            fromLocationId = from,
            fromLocationType = "outlet",
            toLocationId = to,
            toLocationType = "outlet",
            transferDate = DateTime.UtcNow,
            items = lines.Select(line => new { variantId = line.Variant, quantity = line.Quantity }).ToArray()
        });
        return created.GetProperty("id").GetInt64();
    }

    private static async Task<long> DispatchAsync(HttpClient owner, params (long Variant, decimal Quantity)[] lines)
    {
        var id = await CreateTransferAsync(owner, UomPostgresFixture.Outlet, UomPostgresFixture.OutletTwo, lines);
        await Send(owner, HttpMethod.Post, $"/api/stock-transfers/{id}/submit-and-dispatch", new { });
        return id;
    }

    private static async Task<JsonElement> InTransitRowsAsync(HttpClient client, string query) =>
        (await Data(client, $"{Dashboard}/in-transit?{Period}&pageSize=100{query}")).GetProperty("rows").GetProperty("items");

    [Fact]
    public async Task InTransit_DispatchedFabricAttarAndPieces_ShowSeparatelyByUnit_AndReceiptSettlesWithoutDoubleCounting()
    {
        var owner = Owner();
        var fabric = await StockedFabricAsync(owner, "ITF");
        var (attar, _) = await StockedAttarAsync(owner, "ITA");
        var pieces = await _fixture.CreateVariantAsync("ITP");
        await ReceivePcsAsync(owner, pieces, 10m, 20m);
        var before = await DashboardAsync(owner);

        var transferId = await DispatchAsync(owner, (fabric, 200m), (attar, 50m), (pieces, 4m));
        var dispatched = await DashboardAsync(owner);

        // On hand falls by what left; in transit rises by the same; each unit stays its own figure.
        Assert.Equal(-200m, Change(dispatched.OnHand, before.OnHand, "m"));
        Assert.Equal(-50m, Change(dispatched.OnHand, before.OnHand, "ml"));
        Assert.Equal(-4m, Change(dispatched.OnHand, before.OnHand, "pcs"));
        Assert.Equal(200m, Change(dispatched.InTransit, before.InTransit, "m"));
        Assert.Equal(50m, Change(dispatched.InTransit, before.InTransit, "ml"));
        Assert.Equal(4m, Change(dispatched.InTransit, before.InTransit, "pcs"));
        Assert.Equal(3, dispatched.InTransitLines - before.InTransitLines);
        foreach (var unit in new[] { "m", "ml", "pcs" })
            Assert.Equal(0m, Change(dispatched.Accounted, before.Accounted, unit));

        // ৳2,000 of fabric, ৳100 of attar (৳2/ml) and ৳80 of pieces moved from on-hand value to in-transit value.
        Assert.Equal(2180m, dispatched.InTransitValue - before.InTransitValue);
        Assert.Equal(-2180m, dispatched.OnHandValue - before.OnHandValue);
        Assert.Equal(before.AccountedValue, dispatched.AccountedValue);

        // Location view: incoming at the destination, outgoing at the source; the destination's total includes it.
        var destination = await Data(owner, $"{Dashboard}/summary?{Period}{At(UomPostgresFixture.OutletTwo)}");
        var source = await Data(owner, $"{Dashboard}/summary?{Period}{At(UomPostgresFixture.Outlet)}");
        Assert.False(destination.GetProperty("inTransit").GetProperty("businessWide").GetBoolean());
        Assert.True(Units(destination.GetProperty("inTransit").GetProperty("incoming").GetProperty("quantityByUnit")).GetValueOrDefault("m") >= 200m);
        Assert.True(Units(source.GetProperty("inTransit").GetProperty("outgoing").GetProperty("quantityByUnit")).GetValueOrDefault("m") >= 200m);

        // By location: each line once at each end, and once in the business-wide figure.
        var byLocation = await Data(owner, $"{Dashboard}/locations?{Period}");
        JsonElement Row(long id) => byLocation.GetProperty("locations").EnumerateArray()
            .Single(row => row.GetProperty("locationId").GetInt64() == id && row.GetProperty("locationType").GetString() == "outlet");
        Assert.Equal(dispatched.InTransit, Units(byLocation.GetProperty("inTransit").GetProperty("quantityByUnit")));
        Assert.Equal(dispatched.InTransitValue, Subtotal(byLocation.GetProperty("inTransit").GetProperty("valuation")));
        Assert.True(Units(Row(UomPostgresFixture.OutletTwo).GetProperty("incoming").GetProperty("quantityByUnit")).GetValueOrDefault("ml") >= 50m);
        Assert.True(Units(Row(UomPostgresFixture.Outlet).GetProperty("outgoing").GetProperty("quantityByUnit")).GetValueOrDefault("ml") >= 50m);

        // The lines themselves, found by product.
        var rows = await InTransitRowsAsync(owner, "&search=ITF");
        var fabricRow = Assert.Single(rows.EnumerateArray(), row => row.GetProperty("transferId").GetInt64() == transferId);
        Assert.Equal(200m, fabricRow.GetProperty("quantity").GetDecimal());
        Assert.Equal("m", fabricRow.GetProperty("baseUnitCode").GetString());
        Assert.Equal(2000m, fabricRow.GetProperty("value").GetDecimal());
        Assert.Equal(UomPostgresFixture.OutletTwo, fabricRow.GetProperty("toLocationId").GetInt64());

        // Receipt settles it: in transit returns to where it was and the destination holds the stock.
        await Send(OutletTwoReceiver(), HttpMethod.Post, $"/api/stock-transfers/{transferId}/receive", new { });
        var received = await DashboardAsync(owner);
        foreach (var unit in new[] { "m", "ml", "pcs" })
        {
            Assert.Equal(0m, Change(received.InTransit, before.InTransit, unit));
            Assert.Equal(0m, Change(received.OnHand, before.OnHand, unit));
            Assert.Equal(0m, Change(received.Accounted, before.Accounted, unit));
        }
        Assert.Equal(before.InTransitValue, received.InTransitValue);
        Assert.Equal(before.AccountedValue, received.AccountedValue);
        Assert.DoesNotContain((await InTransitRowsAsync(owner, "&search=ITF")).EnumerateArray(), row => row.GetProperty("transferId").GetInt64() == transferId);
    }

    [Fact]
    public async Task InTransit_PartialReceipt_SettlesTheOriginal_RejectedUnitsTravelBack_LostUnitsLeave()
    {
        var owner = Owner();
        var pieces = await _fixture.CreateVariantAsync("ITR");
        await ReceivePcsAsync(owner, pieces, 10m, 20m);
        var before = await DashboardAsync(owner);
        var transferId = await DispatchAsync(owner, (pieces, 10m));

        await Send(OutletTwoReceiver(), HttpMethod.Post, $"/api/stock-transfers/{transferId}/receive", new
        {
            items = new[] { new { variantId = pieces, acceptedQuantity = 6m, rejectedQuantity = 3m, missingQuantity = 1m, damagedQuantity = 0m, remarks = "One short" } }
        });

        // 6 now on hand at the destination, 3 on their way back to the source, 1 recorded missing.
        var settled = await DashboardAsync(owner);
        Assert.Equal(-4m, Change(settled.OnHand, before.OnHand, "pcs"));
        Assert.Equal(3m, Change(settled.InTransit, before.InTransit, "pcs"));
        Assert.Equal(-1m, Change(settled.Accounted, before.Accounted, "pcs"));
        Assert.Equal(60m, settled.InTransitValue - before.InTransitValue);
        Assert.Equal(-20m, settled.AccountedValue - before.AccountedValue);

        var rows = await InTransitRowsAsync(owner, "&search=ITR");
        var back = Assert.Single(rows.EnumerateArray());
        Assert.True(back.GetProperty("isReturn").GetBoolean());
        Assert.Equal(UomPostgresFixture.Outlet, back.GetProperty("toLocationId").GetInt64());
        Assert.NotEqual(transferId, back.GetProperty("transferId").GetInt64());

        // The return is incoming at the source, outgoing at the destination.
        var source = await Data(owner, $"{Dashboard}/in-transit?{Period}&search=ITR&direction=incoming{At(UomPostgresFixture.Outlet)}");
        Assert.Single(source.GetProperty("rows").GetProperty("items").EnumerateArray());
        var destination = await Data(owner, $"{Dashboard}/in-transit?{Period}&search=ITR&direction=incoming{At(UomPostgresFixture.OutletTwo)}");
        Assert.Empty(destination.GetProperty("rows").GetProperty("items").EnumerateArray());

        await Send(owner, HttpMethod.Post, $"/api/stock-transfers/{back.GetProperty("transferId").GetInt64()}/receive", new { });
        var returned = await DashboardAsync(owner);
        Assert.Equal(0m, Change(returned.InTransit, before.InTransit, "pcs"));
        Assert.Equal(-1m, Change(returned.OnHand, before.OnHand, "pcs"));
        Assert.Equal(before.InTransitValue, returned.InTransitValue);
        Assert.Equal(-20m, returned.AccountedValue - before.AccountedValue);
    }

    [Fact]
    public async Task InTransit_RejectedTransfer_IsInTransitBackToTheSource_AtItsFullValue()
    {
        var owner = Owner();
        var pieces = await _fixture.CreateVariantAsync("ITJ");
        await ReceivePcsAsync(owner, pieces, 5m, 30m);
        var before = await DashboardAsync(owner);
        var transferId = await DispatchAsync(owner, (pieces, 5m));

        await Send(OutletTwoReceiver(), HttpMethod.Post, $"/api/stock-transfers/{transferId}/reject", new { reason = "Wrong shipment" });

        var rejected = await DashboardAsync(owner);
        Assert.Equal(5m, Change(rejected.InTransit, before.InTransit, "pcs"));
        Assert.Equal(0m, Change(rejected.Accounted, before.Accounted, "pcs"));
        Assert.Equal(150m, rejected.InTransitValue - before.InTransitValue);
        Assert.Equal(before.AccountedValue, rejected.AccountedValue);
        var back = Assert.Single((await InTransitRowsAsync(owner, "&search=ITJ")).EnumerateArray());
        Assert.True(back.GetProperty("isReturn").GetBoolean());
        Assert.Equal(150m, back.GetProperty("value").GetDecimal());
    }

    [Fact]
    public async Task InTransit_NeverCountsDraftsApprovalsOrCancelledTransfers()
    {
        var owner = Owner();
        var pieces = await _fixture.CreateVariantAsync("ITD");
        await ReceivePcsAsync(owner, pieces, 10m, 20m);
        var before = await DashboardAsync(owner);

        await CreateTransferAsync(owner, UomPostgresFixture.Outlet, UomPostgresFixture.OutletTwo, (pieces, 1m));
        var approved = await CreateTransferAsync(owner, UomPostgresFixture.Outlet, UomPostgresFixture.OutletTwo, (pieces, 2m));
        await Send(owner, HttpMethod.Post, $"/api/stock-transfers/{approved}/submit", new { });
        await Send(owner, HttpMethod.Post, $"/api/stock-transfers/{approved}/approve", new { });
        var cancelled = await CreateTransferAsync(owner, UomPostgresFixture.Outlet, UomPostgresFixture.OutletTwo, (pieces, 3m));
        await Send(owner, HttpMethod.Post, $"/api/stock-transfers/{cancelled}/cancel", new { reason = "Not needed" });

        var after = await DashboardAsync(owner);
        Assert.Equal(before.InTransitLines, after.InTransitLines);
        Assert.Equal(0m, Change(after.InTransit, before.InTransit, "pcs"));
        Assert.Equal(0m, Change(after.OnHand, before.OnHand, "pcs"));
        Assert.Empty((await InTransitRowsAsync(owner, "&search=ITD")).EnumerateArray());
    }

    [Fact]
    public async Task InTransit_ConcurrentReceipts_SettleOnce()
    {
        var owner = Owner();
        var pieces = await _fixture.CreateVariantAsync("ITC");
        await ReceivePcsAsync(owner, pieces, 8m, 20m);
        var transferId = await DispatchAsync(owner, (pieces, 8m));
        var before = await DashboardAsync(owner);

        var results = await Task.WhenAll(Enumerable.Range(0, 3).Select(async _ =>
        {
            using var response = await OutletTwoReceiver().PostAsJsonAsync($"/api/stock-transfers/{transferId}/receive", new { });
            return response.IsSuccessStatusCode;
        }));
        Assert.Equal(1, results.Count(ok => ok));

        var after = await DashboardAsync(owner);
        Assert.Equal(-8m, Change(after.InTransit, before.InTransit, "pcs"));
        Assert.Equal(8m, Change(after.OnHand, before.OnHand, "pcs"));
        Assert.Equal(0m, Change(after.Accounted, before.Accounted, "pcs"));
        Assert.Equal(before.AccountedValue, after.AccountedValue);
    }

    [Fact]
    public async Task InTransit_Endpoint_ChecksDirectionLocationAndPermission()
    {
        var owner = Owner();
        using (var noLocation = await owner.GetAsync($"{Dashboard}/in-transit?{Period}&direction=incoming"))
            Assert.Equal(HttpStatusCode.BadRequest, noLocation.StatusCode);
        using (var unknown = await owner.GetAsync($"{Dashboard}/in-transit?{Period}&direction=sideways"))
            Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        using (var foreign = await owner.GetAsync($"{Dashboard}/in-transit?{Period}&locationId={UomPostgresFixture.ForeignOutlet}&locationType=outlet"))
            Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);

        var clerk = _fixture.Client(UomPostgresFixture.OwnerId, "Manager", "stock_transfers.view");
        using (var denied = await clerk.GetAsync($"{Dashboard}/in-transit?{Period}"))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }
}
