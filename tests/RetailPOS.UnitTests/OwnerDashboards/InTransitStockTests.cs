using Moq;
using RetailPOS.Application.OwnerDashboards;

namespace RetailPOS.Tests.OwnerDashboards;

/// <summary>On hand, in transit and total accounted stock stay separate figures, per unit, and nothing counts twice.</summary>
public sealed class InTransitStockTests
{
    private static readonly DateOnly From = new(2026, 10, 1);
    private static readonly DateOnly To = new(2026, 10, 30);

    private static readonly OwnerDashboardScope Scope = new(
        1,
        "Asia/Dhaka",
        new DateOnly(2026, 11, 6),
        [new OwnerDashboardLocation(5, "Outlet 5", OwnerDashboardLocationTypes.Outlet), new OwnerDashboardLocation(1, "Basundhara 1", OwnerDashboardLocationTypes.Outlet)],
        [new OwnerDashboardLocation(9, "Warehouse", OwnerDashboardLocationTypes.Warehouse)],
        [5],
        1,
        true,
        true);

    private static InTransitLine Line(long transfer, decimal quantity, string unit, decimal? value, long from = 9, string fromType = "warehouse", long to = 1) =>
        new(transfer, $"TRF-{transfer}", false, 100 + transfer, 7, "FAB", "Fabric", $"SKU-{transfer}", "Default", 3, "Cloth",
            from, fromType, "From", to, OwnerDashboardLocationTypes.Outlet, "To", quantity, unit, value, new DateTime(2026, 11, 5, 0, 0, 0, DateTimeKind.Utc));

    private static VariantLocationFact Stock(long location, decimal onHand, string unit, decimal? value) =>
        TestFacts.Outlet(location, 0, 0, 30) with { OnHand = onHand, StockValue = value, BaseUnitCode = unit };

    private static Mock<IInventoryDashboardReadStore> Store(VariantLocationFact[] facts, InTransitLine[] transit)
    {
        var store = new Mock<IInventoryDashboardReadStore>();
        store.Setup(item => item.GetSnapshotAsync(
                It.IsAny<OwnerDashboardScope>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InventorySnapshot(facts, new DateTime(2026, 11, 6, 0, 0, 0, DateTimeKind.Utc), transit));
        return store;
    }

    private static Mock<IOwnerDashboardScopeProvider> Provider()
    {
        var provider = new Mock<IOwnerDashboardScopeProvider>();
        provider.Setup(item => item.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Scope);
        return provider;
    }

    private static Task<InventorySummaryReadModel> SummaryAsync(VariantLocationFact[] facts, InTransitLine[] transit, long? location = null, string? type = null) =>
        new GetInventorySummaryQueryHandler(Provider().Object, Store(facts, transit).Object, new OwnerDashboardPolicy())
            .Handle(new GetInventorySummaryQuery(new InventoryFilter(From, To, location, type, null, null)), CancellationToken.None);

    private static decimal Qty(IReadOnlyList<UnitQuantity>? rows, string unit) =>
        rows?.SingleOrDefault(row => row.UnitCode == unit)?.Quantity ?? 0m;

    // The reported example: 300 m, 450 ml and 4,560 pcs on hand; 200 m and 50 ml dispatched to Basundhara 1.
    private static readonly VariantLocationFact[] OnHand =
    [
        Stock(5, 300m, "m", 3000m),
        Stock(5, 450m, "ml", 900m),
        Stock(5, 4560m, "pcs", 45600m)
    ];

    private static readonly InTransitLine[] Dispatched =
    [
        Line(1, 200m, "m", 2000m, from: 5, fromType: OwnerDashboardLocationTypes.Outlet),
        Line(2, 50m, "ml", 100m, from: 5, fromType: OwnerDashboardLocationTypes.Outlet)
    ];

    [Fact]
    public async Task AllLocations_ShowOnHandInTransitAndTotal_PerUnit_WithoutAddingUnits()
    {
        var result = await SummaryAsync(OnHand, Dispatched);

        Assert.Equal(300m, Qty(result.QuantityByUnit, "m"));
        Assert.Equal(4560m, result.TotalStockQuantity);
        Assert.True(result.InTransit!.BusinessWide);
        Assert.Same(result.InTransit.Incoming, result.InTransit.Outgoing);
        Assert.Equal(200m, Qty(result.InTransit.Incoming.QuantityByUnit, "m"));
        Assert.Equal(50m, Qty(result.InTransit.Incoming.QuantityByUnit, "ml"));
        Assert.Equal(0m, Qty(result.InTransit.Incoming.QuantityByUnit, "pcs"));
        Assert.Equal(2, result.InTransit.Incoming.Transfers);
        Assert.Equal(500m, Qty(result.TotalAccountedByUnit, "m"));
        Assert.Equal(500m, Qty(result.TotalAccountedByUnit, "ml"));
        Assert.Equal(4560m, Qty(result.TotalAccountedByUnit, "pcs"));

        // On-hand value is unchanged; in transit is shown beside it; the total adds them once.
        Assert.Equal(49500m, result.TotalStockValue.Value);
        Assert.Equal(2100m, result.InTransit.Incoming.Valuation.ValuedSubtotal);
        Assert.Equal(51600m, result.TotalAccountedValuation!.ValuedSubtotal);
    }

    [Fact]
    public async Task OneLocation_SplitsIncomingFromOutgoing_AndOnlyIncomingJoinsItsTotal()
    {
        var destination = await SummaryAsync(OnHand, Dispatched, 1, OwnerDashboardLocationTypes.Outlet);
        var source = await SummaryAsync(OnHand, Dispatched, 5, OwnerDashboardLocationTypes.Outlet);

        Assert.False(destination.InTransit!.BusinessWide);
        Assert.Equal(200m, Qty(destination.InTransit.Incoming.QuantityByUnit, "m"));
        Assert.Empty(destination.InTransit.Outgoing.QuantityByUnit);
        Assert.Equal(200m, Qty(destination.TotalAccountedByUnit, "m"));

        Assert.Empty(source.InTransit!.Incoming.QuantityByUnit);
        Assert.Equal(200m, Qty(source.InTransit.Outgoing.QuantityByUnit, "m"));
        Assert.Equal(300m, Qty(source.TotalAccountedByUnit, "m"));

        // The two locations add up to the business total exactly: the 200 m counts at its destination only.
        Assert.Equal(500m, Qty(destination.TotalAccountedByUnit, "m") + Qty(source.TotalAccountedByUnit, "m"));
    }

    [Fact]
    public async Task UnknownTransitCost_StaysOutOfTheValue_AndIsWarned()
    {
        var result = await SummaryAsync(OnHand, [Line(1, 200m, "m", 2000m), Line(2, 50m, "ml", null)]);

        Assert.Equal(StockValuationStatuses.Partial, result.InTransit!.Incoming.Valuation.Status);
        Assert.Equal(2000m, result.InTransit.Incoming.Valuation.ValuedSubtotal);
        Assert.Equal(50m, result.InTransit.Incoming.Valuation.UnvaluedQuantity);
        Assert.Equal(StockValuationStatuses.Partial, result.TotalAccountedValuation!.Status);
        Assert.Contains(result.DataQualityWarnings, warning => warning.Contains("in-transit line(s) have no reliable cost"));
    }

    [Fact]
    public async Task ByLocation_ListsEachEnd_AndTheBusinessFigureOnce()
    {
        var handler = new GetStockValueByLocationQueryHandler(Provider().Object, Store(OnHand, Dispatched).Object);

        var result = await handler.Handle(new GetStockValueByLocationQuery(new InventoryFilter(From, To, null, null, null, null)), CancellationToken.None);

        var outlet5 = result.Locations.Single(row => row.LocationId == 5);
        var basundhara = result.Locations.Single(row => row.LocationId == 1);
        Assert.Equal(200m, Qty(outlet5.Outgoing!.QuantityByUnit, "m"));
        Assert.Equal(0, outlet5.Incoming!.Lines);
        Assert.Equal(200m, Qty(basundhara.Incoming!.QuantityByUnit, "m"));
        Assert.Equal(2, result.InTransit!.Lines);
        Assert.Equal(2100m, result.InTransit.Valuation.ValuedSubtotal);
        // On-hand value by location is unchanged by stock in transit.
        Assert.Equal(49500m, result.TotalStockValue);
    }

    [Theory]
    [InlineData("incoming", null, null)]
    [InlineData("sideways", 1L, "outlet")]
    public async Task List_RejectsADirectionWithoutLocation_OrAnUnknownOne(string direction, long? location, string? type)
    {
        var handler = new GetInTransitStockQueryHandler(Provider().Object, Store(OnHand, Dispatched).Object);

        await Assert.ThrowsAsync<ArgumentException>(() => handler.Handle(
            new GetInTransitStockQuery(new InventoryFilter(From, To, location, type, null, null), direction, 1, 15), CancellationToken.None));
    }

    [Fact]
    public async Task List_FiltersByDirection_AndOrdersOldestDispatchFirst()
    {
        var older = Line(3, 4m, "pcs", 80m, from: 1, fromType: OwnerDashboardLocationTypes.Outlet, to: 5) with
        {
            ToLocationType = OwnerDashboardLocationTypes.Outlet,
            DispatchedAtUtc = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        var handler = new GetInTransitStockQueryHandler(Provider().Object, Store(OnHand, [.. Dispatched, older]).Object);

        var all = await handler.Handle(new GetInTransitStockQuery(new InventoryFilter(From, To, null, null, null, null), null, 1, 15), CancellationToken.None);
        var outgoing = await handler.Handle(
            new GetInTransitStockQuery(new InventoryFilter(From, To, 1, OwnerDashboardLocationTypes.Outlet, null, null), "outgoing", 1, 15), CancellationToken.None);

        Assert.Equal([3L, 1L, 2L], all.Rows.Items.Select(row => row.TransferId));
        Assert.Equal(3L, Assert.Single(outgoing.Rows.Items).TransferId);
        Assert.Equal(200m, Qty(outgoing.Summary.Incoming.QuantityByUnit, "m"));
    }
}
