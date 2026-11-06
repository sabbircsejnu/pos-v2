using Moq;
using RetailPOS.Application.Outlets.SalesTargets;
using RetailPOS.Application.OwnerDashboards;

namespace RetailPOS.Tests.OwnerDashboards;

public sealed class OwnerDashboardHandlerTests
{
    private static readonly DateOnly From = new(2026, 10, 1);
    private static readonly DateOnly To = new(2026, 10, 30);

    private static OwnerDashboardScope Scope(bool create = true, bool anyLocation = true) =>
        new(
            1,
            "Asia/Dhaka",
            new DateOnly(2026, 11, 6),
            [new OwnerDashboardLocation(5, "Outlet 5", OwnerDashboardLocationTypes.Outlet), new OwnerDashboardLocation(1, "Outlet 1", OwnerDashboardLocationTypes.Outlet)],
            [new OwnerDashboardLocation(9, "Warehouse", OwnerDashboardLocationTypes.Warehouse)],
            [5],
            1,
            create,
            anyLocation);

    private static Mock<IOwnerDashboardScopeProvider> Provider(OwnerDashboardScope scope)
    {
        var provider = new Mock<IOwnerDashboardScopeProvider>();
        provider.Setup(item => item.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(scope);
        return provider;
    }

    private static Mock<ISalesFinanceReadStore> SalesStore(decimal expense = 0m)
    {
        var store = new Mock<ISalesFinanceReadStore>(MockBehavior.Strict);
        store.Setup(item => item.GetSalesFactsAsync(
                It.IsAny<OwnerDashboardScope>(), It.IsAny<List<long>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SalesFacts([new SalesDayFact(5, From, 1000m, 10, 600m, 0)], 0, 0));
        store.Setup(item => item.GetPurchaseValueAsync(
                It.IsAny<OwnerDashboardScope>(), It.IsAny<List<long>>(), It.IsAny<List<long>>(), From, To, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0m);
        store.Setup(item => item.GetApprovedExpenseValueAsync(
                It.IsAny<OwnerDashboardScope>(), It.IsAny<List<long>>(), It.IsAny<List<long>>(), It.IsAny<bool>(), From, To, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expense);
        return store;
    }

    private static Mock<IInventoryDashboardReadStore> InventoryStore(params VariantLocationFact[] facts)
    {
        var store = new Mock<IInventoryDashboardReadStore>();
        store.Setup(item => item.GetSnapshotAsync(
                It.IsAny<OwnerDashboardScope>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InventorySnapshot(facts, new DateTime(2026, 11, 6, 0, 0, 0, DateTimeKind.Utc)));
        return store;
    }

    private static InventoryFilter Filter(long? locationId = null, string? type = null, string? search = null) =>
        new(From, To, locationId, type, null, search);

    private static Mock<IOutletSalesTargetReadStore> TargetStore(params OutletSalesTargetSchedule[] schedules)
    {
        var store = new Mock<IOutletSalesTargetReadStore>();
        store.Setup(item => item.GetSchedulesAsync(It.IsAny<long>(), It.IsAny<IReadOnlyCollection<long>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(schedules.ToDictionary(schedule => schedule.OutletId));
        return store;
    }

    private static GetSalesFinanceSummaryQueryHandler SummaryHandler(
        Mock<ISalesFinanceReadStore> store,
        OwnerDashboardPolicy? policy = null) =>
        new(Provider(Scope()).Object, store.Object, TargetStore().Object, policy ?? new OwnerDashboardPolicy(), TimeProvider.System);

    [Fact]
    public async Task Summary_ProfitIsSalesLessCogsLessExpense_FromOneScope()
    {
        var result = await SummaryHandler(SalesStore(expense: 150m))
            .Handle(new GetSalesFinanceSummaryQuery(From, To, null), CancellationToken.None);

        Assert.Equal(250m, result.Profit.Value);
        Assert.Equal(MetricStatuses.Estimated, result.Profit.Status);
    }

    [Fact]
    public async Task Summary_WithoutTargets_ReportsNotConfiguredNotZero()
    {
        var result = await SummaryHandler(SalesStore())
            .Handle(new GetSalesFinanceSummaryQuery(From, To, null), CancellationToken.None);

        Assert.Equal(MetricStatuses.NotConfigured, result.Target.Status);
        Assert.Null(result.Target.Value);
        Assert.Null(result.Shortfall.Value);
        Assert.Null(result.AchievementPercent);
    }

    [Fact]
    public async Task Summary_ForAllLocations_ReadsEverySalesAndExpenseLocationOfTheBusiness()
    {
        var store = SalesStore();

        await SummaryHandler(store).Handle(new GetSalesFinanceSummaryQuery(From, To, null), CancellationToken.None);

        store.Verify(item => item.GetSalesFactsAsync(
            It.IsAny<OwnerDashboardScope>(),
            It.Is<List<long>>(ids => ids.Count == 2 && ids.Contains(5) && ids.Contains(1)),
            From,
            To,
            It.IsAny<CancellationToken>()));
        store.Verify(item => item.GetApprovedExpenseValueAsync(
            It.IsAny<OwnerDashboardScope>(),
            It.Is<List<long>>(ids => ids.Count == 2 && ids.Contains(5) && ids.Contains(1)),
            It.Is<List<long>>(ids => ids.Count == 1 && ids[0] == 9),
            true,
            From,
            To,
            It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task Summary_ForAShowroom_NarrowsSalesCogsAndExpensesAlike()
    {
        var store = SalesStore();

        await SummaryHandler(store).Handle(new GetSalesFinanceSummaryQuery(From, To, 5), CancellationToken.None);

        store.Verify(item => item.GetSalesFactsAsync(
            It.IsAny<OwnerDashboardScope>(),
            It.Is<List<long>>(ids => ids.Count == 1 && ids[0] == 5),
            From,
            To,
            It.IsAny<CancellationToken>()));
        store.Verify(item => item.GetApprovedExpenseValueAsync(
            It.IsAny<OwnerDashboardScope>(),
            It.Is<List<long>>(ids => ids.Count == 1 && ids[0] == 5),
            It.Is<List<long>>(ids => ids.Count == 0),
            false,
            From,
            To,
            It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task Summary_WithProfitSwitchedOff_IsDisabled()
    {
        var result = await SummaryHandler(SalesStore(), new OwnerDashboardPolicy { ProfitEnabled = false })
            .Handle(new GetSalesFinanceSummaryQuery(From, To, null), CancellationToken.None);

        Assert.Equal(MetricStatuses.Disabled, result.Profit.Status);
    }

    [Fact]
    public async Task Summary_ForALocationOutsideTheBusiness_IsForbiddenBeforeAnyRead()
    {
        var store = new Mock<ISalesFinanceReadStore>(MockBehavior.Strict);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            SummaryHandler(store).Handle(new GetSalesFinanceSummaryQuery(From, To, 999), CancellationToken.None));
    }

    [Fact]
    public async Task InvertedRange_IsRejectedBeforeAnyRead()
    {
        var provider = new Mock<IOwnerDashboardScopeProvider>(MockBehavior.Strict);
        var handler = new GetSalesFinanceSummaryQueryHandler(
            provider.Object, new Mock<ISalesFinanceReadStore>(MockBehavior.Strict).Object, TargetStore().Object, new OwnerDashboardPolicy(), TimeProvider.System);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(new GetSalesFinanceSummaryQuery(To, From, null), CancellationToken.None));
    }

    [Fact]
    public async Task Inventory_ReportsStockValueToEveryHolder()
    {
        var store = InventoryStore(TestFacts.Outlet(5, 10, 0, 30));
        var handler = new GetInventorySummaryQueryHandler(Provider(Scope()).Object, store.Object, new OwnerDashboardPolicy());

        var result = await handler.Handle(new GetInventorySummaryQuery(Filter()), CancellationToken.None);

        Assert.Equal(500m, result.TotalStockValue.Value);
        Assert.Equal(MetricStatuses.Ok, result.TotalStockValue.Status);
    }

    [Fact]
    public async Task Inventory_NegativeStock_IsFlaggedAndNotCounted()
    {
        var store = InventoryStore(
            TestFacts.Outlet(5, 10, 0, 30),
            TestFacts.Outlet(1, 0, 0, 30) with { OnHand = -4, StockValue = 0m });
        var handler = new GetInventorySummaryQueryHandler(Provider(Scope()).Object, store.Object, new OwnerDashboardPolicy());

        var result = await handler.Handle(new GetInventorySummaryQuery(Filter()), CancellationToken.None);

        Assert.Equal(10, result.TotalStockQuantity);
        Assert.Equal(1, result.NegativeStockRows);
        Assert.Contains(result.DataQualityWarnings, warning => warning.Contains("negative"));
    }

    [Fact]
    public async Task Inventory_ProductCounts_DoNotInflateAcrossLocations()
    {
        var store = InventoryStore(TestFacts.Outlet(5, 10, 0, 30), TestFacts.Outlet(1, 4, 0, 30));
        var handler = new GetInventorySummaryQueryHandler(Provider(Scope()).Object, store.Object, new OwnerDashboardPolicy());

        var result = await handler.Handle(new GetInventorySummaryQuery(Filter()), CancellationToken.None);

        Assert.Equal(1, result.ActiveStockedProducts);
        Assert.Equal(1, result.StockedVariants);
    }

    [Fact]
    public async Task Inventory_ForALocationOutsideTheBusiness_IsForbidden()
    {
        var handler = new GetInventorySummaryQueryHandler(
            Provider(Scope()).Object, InventoryStore().Object, new OwnerDashboardPolicy());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handler.Handle(
            new GetInventorySummaryQuery(Filter(999, OwnerDashboardLocationTypes.Outlet)), CancellationToken.None));
    }

    [Fact]
    public async Task Inventory_WithLocationButNoType_IsRejected()
    {
        var handler = new GetInventorySummaryQueryHandler(
            Provider(Scope()).Object, InventoryStore().Object, new OwnerDashboardPolicy());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(new GetInventorySummaryQuery(Filter(5)), CancellationToken.None));
    }

    [Theory]
    [InlineData(0, 15)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task Paging_IsBounded(int page, int pageSize)
    {
        var handler = new GetBestSellersQueryHandler(Provider(Scope()).Object, InventoryStore().Object);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(new GetBestSellersQuery(Filter(), null, null, page, pageSize), CancellationToken.None));
    }

    [Fact]
    public async Task BestSellers_AggregateByExactVariant_AndTieBreakStably()
    {
        var store = InventoryStore(
            TestFacts.Outlet(5, 3, 2, 30),
            TestFacts.Outlet(1, 4, 3, 30),
            TestFacts.Outlet(1, 1, 5, 30) with { VariantId = 12, Sku = "A-SKU", NetSales = 100m },
            TestFacts.Outlet(1, 9, 0, 30) with { VariantId = 13, Sku = "NO-SALE" });
        var handler = new GetBestSellersQueryHandler(Provider(Scope()).Object, store.Object);

        var result = await handler.Handle(new GetBestSellersQuery(Filter(), null, null, 1, 15), CancellationToken.None);

        Assert.Equal(2, result.TotalCount); // Zero-unit rows are excluded.
        Assert.Equal("BRK-1005-BLK-XL", result.Items[0].Sku); // 5 units and 500 beats 5 units and 100.
        Assert.Equal(5, result.Items[0].NetUnitsSold);
        Assert.Equal(7, result.Items[0].CurrentAvailableStock);
        Assert.Equal(2, result.Items[1].Rank);
    }

    [Fact]
    public async Task Transfers_WithoutCreatePermission_AreReadOnly()
    {
        var store = InventoryStore(TestFacts.Outlet(5, 42, 3, 30), TestFacts.Outlet(1, 6, 48, 30));
        var handler = new GetTransferOpportunitiesQueryHandler(
            Provider(Scope(create: false)).Object, store.Object, new OwnerDashboardPolicy());

        var result = await handler.Handle(
            new GetTransferOpportunitiesQuery(Filter(), null, null, null, 1, 10), CancellationToken.None);

        Assert.False(Assert.Single(result.Rows.Items).CanInitiate);
    }

    [Theory]
    [InlineData(true, true)]   // May pick any source that is assigned.
    [InlineData(false, false)] // Pinned to default outlet 1, while the source is outlet 5.
    public async Task Transfers_FollowTheFormsSourceRule(bool anyLocation, bool expected)
    {
        var store = InventoryStore(TestFacts.Outlet(5, 42, 3, 30), TestFacts.Outlet(1, 6, 48, 30));
        var handler = new GetTransferOpportunitiesQueryHandler(
            Provider(Scope(anyLocation: anyLocation)).Object, store.Object, new OwnerDashboardPolicy());

        var result = await handler.Handle(
            new GetTransferOpportunitiesQuery(Filter(), null, null, null, 1, 10), CancellationToken.None);

        Assert.Equal(expected, Assert.Single(result.Rows.Items).CanInitiate);
    }

    [Fact]
    public async Task Transfers_FromASourceTheUserIsNotAssigned_CannotBeInitiated()
    {
        var store = InventoryStore(TestFacts.Outlet(5, 42, 3, 30), TestFacts.Outlet(1, 6, 48, 30));
        var handler = new GetTransferOpportunitiesQueryHandler(
            Provider(Scope() with { TransferSourceOutletIds = [1] }).Object, store.Object, new OwnerDashboardPolicy());

        var result = await handler.Handle(
            new GetTransferOpportunitiesQuery(Filter(), null, null, null, 1, 10), CancellationToken.None);

        Assert.False(Assert.Single(result.Rows.Items).CanInitiate);
    }

    [Fact]
    public async Task Transfers_FiltersNarrowTheView_WithoutChangingQuantities()
    {
        var store = InventoryStore(TestFacts.Outlet(5, 42, 3, 30), TestFacts.Outlet(1, 6, 48, 30));
        var handler = new GetTransferOpportunitiesQueryHandler(
            Provider(Scope()).Object, store.Object, new OwnerDashboardPolicy());

        var bySource = await handler.Handle(
            new GetTransferOpportunitiesQuery(Filter(), 5, null, TransferPriorities.High, 1, 10), CancellationToken.None);
        var byOther = await handler.Handle(
            new GetTransferOpportunitiesQuery(Filter(), 1, null, null, 1, 10), CancellationToken.None);

        Assert.Equal(28, Assert.Single(bySource.Rows.Items).SuggestedQty);
        Assert.Empty(byOther.Rows.Items);
    }

    [Fact]
    public async Task Transfers_WhenSwitchedOff_AreDisabled()
    {
        var handler = new GetTransferOpportunitiesQueryHandler(
            Provider(Scope()).Object,
            new Mock<IInventoryDashboardReadStore>(MockBehavior.Strict).Object,
            new OwnerDashboardPolicy { TransferOpportunitiesEnabled = false });

        var result = await handler.Handle(
            new GetTransferOpportunitiesQuery(Filter(), null, null, null, 1, 10), CancellationToken.None);

        Assert.Equal(MetricStatuses.Disabled, result.Status);
        Assert.Empty(result.Rows.Items);
    }

    [Fact]
    public async Task TransferDetail_ForAStaleKey_IsNotFound()
    {
        var handler = new GetTransferOpportunityDetailQueryHandler(
            Provider(Scope()).Object, InventoryStore().Object, new OwnerDashboardPolicy());

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            handler.Handle(new GetTransferOpportunityDetailQuery("11-5-1", From, To), CancellationToken.None));
    }

    [Fact]
    public async Task SlowMovers_ExcludeThinEvidence_AndCountIt()
    {
        var store = InventoryStore(
            TestFacts.Outlet(5, 10, 0, 30),
            TestFacts.Outlet(1, 10, 0, 2));
        var handler = new GetSlowMoversQueryHandler(Provider(Scope()).Object, store.Object, new OwnerDashboardPolicy());

        var result = await handler.Handle(
            new GetSlowMoversQuery(Filter(), null, null, null, null, null, 1, 15), CancellationToken.None);

        Assert.Equal(5, Assert.Single(result.Rows.Items).LocationId);
        Assert.Equal(1, result.InsufficientEvidenceCount);
    }

    [Fact]
    public async Task SlowMovers_RejectUnknownSortColumns()
    {
        var handler = new GetSlowMoversQueryHandler(
            Provider(Scope()).Object, InventoryStore().Object, new OwnerDashboardPolicy());

        await Assert.ThrowsAsync<ArgumentException>(() => handler.Handle(
            new GetSlowMoversQuery(Filter(), null, null, null, "passwordHash", null, 1, 15), CancellationToken.None));
    }

    [Fact]
    public async Task ProductInsights_OutsideTheBusiness_IsNotFound()
    {
        var handler = new GetProductInsightsQueryHandler(
            Provider(Scope()).Object, InventoryStore(TestFacts.Outlet(5, 1, 0, 30)).Object);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            handler.Handle(new GetProductInsightsQuery(404, null, Filter()), CancellationToken.None));
    }
}
