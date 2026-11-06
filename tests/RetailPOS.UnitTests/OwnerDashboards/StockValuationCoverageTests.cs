using Moq;
using RetailPOS.Application.OwnerDashboards;

namespace RetailPOS.Tests.OwnerDashboards;

public class StockValuationCoverageTests
{
    private static readonly DateOnly From = new(2026, 10, 1);
    private static readonly DateOnly To = new(2026, 10, 31);

    private static OwnerDashboardScope Scope() => new(
        1,
        "Asia/Dhaka",
        To,
        [
            new OwnerDashboardLocation(5, "A", OwnerDashboardLocationTypes.Outlet),
            new OwnerDashboardLocation(6, "B", OwnerDashboardLocationTypes.Outlet)
        ],
        [],
        [],
        null,
        false,
        false);

    private static IOwnerDashboardScopeProvider Provider()
    {
        var provider = new Mock<IOwnerDashboardScopeProvider>();
        provider.Setup(item => item.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Scope());
        return provider.Object;
    }

    private static IInventoryDashboardReadStore Store(params VariantLocationFact[] facts)
    {
        var store = new Mock<IInventoryDashboardReadStore>();
        store.Setup(item => item.GetSnapshotAsync(
                It.IsAny<OwnerDashboardScope>(),
                It.IsAny<DateOnly>(),
                It.IsAny<DateOnly>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InventorySnapshot(facts, DateTime.UtcNow));
        return store.Object;
    }

    private static InventoryFilter Filter() => new(From, To, null, null, null, null);

    private static VariantLocationFact Fact(long location, int onHand, decimal? value, long variant = 11) =>
        TestFacts.Outlet(location, onHand, 0, 30) with { StockValue = value, VariantId = variant };

    [Fact]
    public void Coverage_CountsEachKindSeparately()
    {
        var coverage = StockValuationCoverage.Of(
        [
            Fact(5, 10, 500m),
            Fact(5, 3, 0m),
            Fact(5, 7, null),
            Fact(5, 0, 0m),
            Fact(5, -2, 0m)
        ]);

        Assert.Equal(500m, coverage.ValuedSubtotal);
        Assert.Equal(StockValuationStatuses.Partial, coverage.Status);
        Assert.Equal(MetricStatuses.Partial, coverage.MetricStatus);
        Assert.Equal((1, 10L), (coverage.ValuedRecords, coverage.ValuedQuantity));
        Assert.Equal((1, 3L), (coverage.ZeroCostRecords, coverage.ZeroCostQuantity));
        Assert.Equal((1, 7L), (coverage.UnvaluedRecords, coverage.UnvaluedQuantity));
    }

    [Fact]
    public void Coverage_OfOnlyUnknownStock_HasNoSubtotal()
    {
        var coverage = StockValuationCoverage.Of([Fact(5, 7, null)]);

        Assert.Null(coverage.ValuedSubtotal);
        Assert.Equal(StockValuationStatuses.NotValued, coverage.Status);
        Assert.Equal(MetricStatuses.NotValued, coverage.MetricStatus);
    }

    [Fact]
    public void Coverage_OfProvenFreeStock_IsAZeroNotAGap()
    {
        var coverage = StockValuationCoverage.Of([Fact(5, 3, 0m)]);

        Assert.Equal(0m, coverage.ValuedSubtotal);
        Assert.Equal(StockValuationStatuses.ZeroCost, coverage.Status);
        Assert.Equal(MetricStatuses.Ok, coverage.MetricStatus);
    }

    [Fact]
    public void Coverage_WithNoStock_IsNeitherValuedNorUnvalued()
    {
        var coverage = StockValuationCoverage.Of([Fact(5, 0, 0m)]);

        Assert.Equal(StockValuationStatuses.NoStock, coverage.Status);
        Assert.Null(coverage.ValuedSubtotal);
    }

    [Fact]
    public async Task Summary_WithNothingValued_ShowsNoValue()
    {
        var handler = new GetInventorySummaryQueryHandler(Provider(), Store(Fact(5, 7, null)), new OwnerDashboardPolicy());

        var result = await handler.Handle(new GetInventorySummaryQuery(Filter()), CancellationToken.None);

        Assert.Null(result.TotalStockValue.Value);
        Assert.Equal(MetricStatuses.NotValued, result.TotalStockValue.Status);
        Assert.Null(result.SlowMovingStockValue.Value);
        Assert.Equal(MetricStatuses.NotValued, result.SlowMovingStockValue.Status);
        Assert.Null(result.SlowMovingSharePercent);
    }

    [Fact]
    public async Task SlowValue_FollowsTheSameRules_AndItsShareNeedsCompleteFigures()
    {
        var partial = new GetInventorySummaryQueryHandler(
            Provider(),
            Store(Fact(5, 10, 500m), Fact(6, 7, null, variant: 12)),
            new OwnerDashboardPolicy());

        var result = await partial.Handle(new GetInventorySummaryQuery(Filter()), CancellationToken.None);

        Assert.Equal(500m, result.SlowMovingStockValue.Value);
        Assert.Equal(MetricStatuses.Partial, result.SlowMovingStockValue.Status);
        Assert.Equal((1, 7L), (result.SlowMovingValuation.UnvaluedRecords, result.SlowMovingValuation.UnvaluedQuantity));
        Assert.Null(result.SlowMovingSharePercent);

        var complete = new GetInventorySummaryQueryHandler(Provider(), Store(Fact(5, 10, 500m)), new OwnerDashboardPolicy());
        var completeResult = await complete.Handle(new GetInventorySummaryQuery(Filter()), CancellationToken.None);
        Assert.Equal(100m, completeResult.SlowMovingSharePercent);
    }

    [Fact]
    public async Task Locations_SumToTheSubtotal_WithUnknownAfterKnown()
    {
        var handler = new GetStockValueByLocationQueryHandler(
            Provider(),
            Store(Fact(5, 7, null), Fact(6, 10, 500m, variant: 12), Fact(6, 2, null, variant: 13)));

        var result = await handler.Handle(new GetStockValueByLocationQuery(Filter()), CancellationToken.None);

        Assert.Equal(500m, result.TotalStockValue);
        Assert.Equal(MetricStatuses.Partial, result.ValueStatus);
        Assert.Equal(new long[] { 6, 5 }, result.Locations.Select(row => row.LocationId));
        Assert.Equal(500m, result.Locations[0].StockValue);
        Assert.Equal(100m, result.Locations[0].PercentOfTotal);
        Assert.Equal(StockValuationStatuses.Partial, result.Locations[0].Valuation.Status);
        Assert.Null(result.Locations[1].StockValue);
        Assert.Null(result.Locations[1].PercentOfTotal);
        Assert.Equal(StockValuationStatuses.NotValued, result.Locations[1].Valuation.Status);
    }

    [Theory]
    [InlineData("asc")]
    [InlineData("desc")]
    public async Task SlowMovers_SortUnknownValueLast(string direction)
    {
        var handler = new GetSlowMoversQueryHandler(
            Provider(),
            Store(Fact(5, 7, null), Fact(5, 10, 500m, variant: 12), Fact(5, 3, 20m, variant: 13)),
            new OwnerDashboardPolicy());

        var result = await handler.Handle(
            new GetSlowMoversQuery(Filter(), null, null, null, "stockValue", direction, 1, 15),
            CancellationToken.None);

        Assert.Null(result.Rows.Items[^1].StockValue);
        Assert.Equal(direction == "asc" ? 20m : 500m, result.Rows.Items[0].StockValue);
    }

    [Fact]
    public async Task ProductInsights_WithNothingValued_ShowNoTotal()
    {
        var handler = new GetProductInsightsQueryHandler(Provider(), Store(Fact(5, 7, null)));

        var result = await handler.Handle(new GetProductInsightsQuery(7, null, Filter()), CancellationToken.None);

        Assert.Null(result.TotalStockValue);
        Assert.Equal(StockValuationStatuses.NotValued, result.Valuation.Status);
    }

    [Fact]
    public async Task ProductInsights_ReportPartialCoverage()
    {
        var handler = new GetProductInsightsQueryHandler(
            Provider(),
            Store(Fact(5, 10, 500m), Fact(6, 7, null, variant: 12)));

        var result = await handler.Handle(new GetProductInsightsQuery(7, null, Filter()), CancellationToken.None);

        Assert.Equal(500m, result.TotalStockValue);
        Assert.Equal(StockValuationStatuses.Partial, result.Valuation.Status);
        Assert.Equal(7L, result.Valuation.UnvaluedQuantity);
    }
}
