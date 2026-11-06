using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Outlets.SalesTargets;

namespace RetailPOS.Application.OwnerDashboards;

/// <summary>Request checks shared by every dashboard query. A location filter must belong to the current Business.</summary>
public static class OwnerDashboardRequests
{
    public const int MaxRangeDays = 366;
    public const int MaxPageSize = 100;
    public const int MaxSearchLength = 100;

    public static void RequirePeriod(DateOnly from, DateOnly to)
    {
        if (from == default || to == default)
            throw new ArgumentException("From and To dates are required.");
        if (from > to)
            throw new ArgumentException("From date must be on or before To date.");
        if (to.DayNumber - from.DayNumber + 1 > MaxRangeDays)
            throw new ArgumentException($"The date range cannot exceed {MaxRangeDays} days.");
    }

    public static (int Page, int PageSize) RequirePaging(int page, int pageSize)
    {
        if (page < 1)
            throw new ArgumentException("Page must be 1 or greater.");
        if (pageSize < 1 || pageSize > MaxPageSize)
            throw new ArgumentException($"Page size must be between 1 and {MaxPageSize}.");
        return (page, pageSize);
    }

    public static string? NormalizeSearch(string? search)
    {
        var trimmed = search?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return null;
        if (trimmed.Length > MaxSearchLength)
            throw new ArgumentException($"Search cannot exceed {MaxSearchLength} characters.");
        return trimmed;
    }

    /// <summary>A reporting filter, not an authorization boundary. Omitting it means every showroom of the Business.</summary>
    public static List<long> ResolveShowrooms(OwnerDashboardScope scope, long? outletId)
    {
        if (outletId is null)
            return scope.OutletIds;
        if (!scope.Outlets.Any(outlet => outlet.Id == outletId))
            throw new UnauthorizedAccessException("The selected showroom does not belong to this Business.");
        return [outletId.Value];
    }

    public static string DescribeShowroomScope(OwnerDashboardScope scope, long? outletId) =>
        outletId is null
            ? "all-showrooms"
            : scope.Outlets.First(outlet => outlet.Id == outletId).Name;
}

public sealed record GetSalesFinanceSummaryQuery(DateOnly From, DateOnly To, long? OutletId)
    : IQuery<SalesFinanceSummaryReadModel>;

public sealed class GetSalesFinanceSummaryQueryHandler(
    IOwnerDashboardScopeProvider scopeProvider,
    ISalesFinanceReadStore store,
    IOutletSalesTargetReadStore targetStore,
    OwnerDashboardPolicy policy,
    TimeProvider clock)
    : IRequestHandler<GetSalesFinanceSummaryQuery, SalesFinanceSummaryReadModel>
{
    public async Task<SalesFinanceSummaryReadModel> Handle(
        GetSalesFinanceSummaryQuery request,
        CancellationToken cancellationToken)
    {
        OwnerDashboardRequests.RequirePeriod(request.From, request.To);
        var scope = await scopeProvider.GetAsync(cancellationToken);
        var outletIds = OwnerDashboardRequests.ResolveShowrooms(scope, request.OutletId);

        var facts = await store.GetSalesFactsAsync(scope, outletIds, request.From, request.To, cancellationToken);
        var (previousFrom, previousTo) = SalesFinanceMath.PreviousPeriod(request.From, request.To);
        var previous = await store.GetSalesFactsAsync(scope, outletIds, previousFrom, previousTo, cancellationToken);

        var netSales = facts.Days.Sum(day => day.NetSales);
        var previousNet = previous.Days.Sum(day => day.NetSales);

        var schedules = await targetStore.GetSchedulesAsync(scope.BusinessId, outletIds, cancellationToken);
        var target = AggregateTarget(outletIds, schedules, request.From, request.To, facts.Days);
        var today = await ReadTodayAsync(request, scope, outletIds, schedules, facts, cancellationToken);

        var warnings = new List<string>();
        if (facts.ReversalsWithoutEvidence > 0)
            warnings.Add($"{facts.ReversalsWithoutEvidence} refund or void record(s) have no usable amounts and are not deducted.");
        if (facts.UnreversedExchanges > 0)
            warnings.Add($"{facts.UnreversedExchanges} exchanged sale(s) are still counted because no exchange netting policy is approved.");

        var (purchase, expense, profit) = await ReadFinancialMetricsAsync(
            request, scope, outletIds, netSales, facts, cancellationToken);

        return new SalesFinanceSummaryReadModel(
            new DashboardContext(
                request.From,
                request.To,
                scope.TimeZoneId,
                OwnerDashboardRequests.DescribeShowroomScope(scope, request.OutletId),
                clock.GetUtcNow().UtcDateTime),
            new MetricValue(
                netSales,
                facts.ReversalsWithoutEvidence > 0 || facts.UnreversedExchanges > 0
                    ? MetricStatuses.Partial
                    : MetricStatuses.Ok,
                "Excludes VAT, after discounts and recorded voids and refunds."),
            SalesFinanceMath.ChangePercent(netSales, previousNet),
            new MetricValue(target.Target, target.Status, target.Note),
            target.AchievementPercent,
            new MetricValue(target.Shortfall, target.Status, target.Note),
            new MetricValue(target.Exceeded, target.Status, target.Note),
            purchase,
            expense,
            profit,
            warnings,
            target.Coverage,
            today);
    }

    private static AggregatedSalesTarget AggregateTarget(
        List<long> outletIds,
        IReadOnlyDictionary<long, OutletSalesTargetSchedule> schedules,
        DateOnly from,
        DateOnly to,
        IReadOnlyList<SalesDayFact> days) =>
        SalesTargetPerformance.Aggregate(outletIds
            .Select(outletId => SalesTargetPerformance.ForShowroom(
                schedules.GetValueOrDefault(outletId) ?? OutletSalesTargetSchedule.Empty(outletId), from, to, days))
            .ToList());

    private async Task<TodayTargetReadModel> ReadTodayAsync(
        GetSalesFinanceSummaryQuery request,
        OwnerDashboardScope scope,
        List<long> outletIds,
        IReadOnlyDictionary<long, OutletSalesTargetSchedule> schedules,
        SalesFacts periodFacts,
        CancellationToken cancellationToken)
    {
        var date = scope.Today;
        var days = request.From == date && request.To == date
            ? periodFacts.Days
            : (await store.GetSalesFactsAsync(scope, outletIds, date, date, cancellationToken)).Days;
        var target = AggregateTarget(outletIds, schedules, date, date, days);
        return new TodayTargetReadModel(
            date,
            days.Sum(day => day.NetSales),
            new MetricValue(target.Target, target.Status, target.Note),
            target.AchievementPercent,
            new MetricValue(target.Shortfall, target.Status, target.Note),
            new MetricValue(target.Exceeded, target.Status, target.Note));
    }

    private async Task<(MetricValue Purchase, MetricValue Expense, MetricValue Profit)> ReadFinancialMetricsAsync(
        GetSalesFinanceSummaryQuery request,
        OwnerDashboardScope scope,
        List<long> outletIds,
        decimal netSales,
        SalesFacts facts,
        CancellationToken cancellationToken)
    {
        // One scope for sales, COGS and expenses (SRS §2.1): a selected showroom narrows all three, and only the whole
        // Business view carries warehouse and business-level costs.
        var wholeBusiness = request.OutletId is null;
        var warehouseIds = wholeBusiness ? scope.WarehouseIds : [];

        var purchaseValue = await store.GetPurchaseValueAsync(
            scope,
            outletIds,
            warehouseIds,
            request.From,
            request.To,
            cancellationToken);
        var expenseValue = await store.GetApprovedExpenseValueAsync(
            scope,
            outletIds,
            warehouseIds,
            wholeBusiness,
            request.From,
            request.To,
            cancellationToken);

        var profit = policy.ProfitEnabled
            ? SalesFinanceMath.EstimateProfit(
                netSales,
                facts.Days.Sum(day => day.Cogs),
                facts.Days.Sum(day => day.MissingCostLines),
                facts.ReversalsWithoutEvidence,
                expenseValue)
            : new MetricValue(null, MetricStatuses.Disabled, "Profit is switched off for this deployment.");

        return (
            new MetricValue(purchaseValue, MetricStatuses.Ok, "Completed goods receipts (GRN), excluding VAT."),
            new MetricValue(expenseValue, MetricStatuses.Ok, "Approved operating expenses, excluding VAT."),
            profit);
    }
}

public sealed record GetShowroomPerformanceQuery(DateOnly From, DateOnly To, long? OutletId)
    : IQuery<ShowroomPerformanceReadModel>;

public sealed class GetShowroomPerformanceQueryHandler(
    IOwnerDashboardScopeProvider scopeProvider,
    ISalesFinanceReadStore store,
    IOutletSalesTargetReadStore targetStore,
    TimeProvider clock)
    : IRequestHandler<GetShowroomPerformanceQuery, ShowroomPerformanceReadModel>
{
    public async Task<ShowroomPerformanceReadModel> Handle(
        GetShowroomPerformanceQuery request,
        CancellationToken cancellationToken)
    {
        OwnerDashboardRequests.RequirePeriod(request.From, request.To);
        var scope = await scopeProvider.GetAsync(cancellationToken);
        var outletIds = OwnerDashboardRequests.ResolveShowrooms(scope, request.OutletId);
        var facts = await store.GetSalesFactsAsync(scope, outletIds, request.From, request.To, cancellationToken);
        var salesByOutlet = facts.Days
            .GroupBy(day => day.OutletId)
            .ToDictionary(group => group.Key, group => group.Sum(day => day.NetSales));
        var schedules = await targetStore.GetSchedulesAsync(scope.BusinessId, outletIds, cancellationToken);

        var rows = scope.Outlets
            .Where(outlet => outletIds.Contains(outlet.Id))
            .Select(outlet =>
            {
                var netSales = salesByOutlet.GetValueOrDefault(outlet.Id);
                var figures = SalesTargetPerformance.ForShowroom(
                    schedules.GetValueOrDefault(outlet.Id) ?? OutletSalesTargetSchedule.Empty(outlet.Id),
                    request.From,
                    request.To,
                    facts.Days);
                var target = SalesTargetPerformance.Evaluate(figures.ComparableSales, figures.Period.Target, figures.Status);
                return new ShowroomPerformanceRow(
                    outlet.Id,
                    outlet.Name,
                    netSales,
                    figures.Period.Target,
                    target.AchievementPercent,
                    target.Shortfall,
                    target.Exceeded,
                    target.Status);
            })
            .OrderByDescending(row => row.NetSales)
            .ThenBy(row => row.OutletName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ShowroomPerformanceReadModel(
            new DashboardContext(
                request.From,
                request.To,
                scope.TimeZoneId,
                OwnerDashboardRequests.DescribeShowroomScope(scope, request.OutletId),
                clock.GetUtcNow().UtcDateTime),
            rows);
    }
}

public sealed record GetCashFlowQuery(DateOnly From, DateOnly To, long? OutletId) : IQuery<CashFlowReadModel>;

public sealed class GetCashFlowQueryHandler(
    IOwnerDashboardScopeProvider scopeProvider,
    ISalesFinanceReadStore store,
    TimeProvider clock)
    : IRequestHandler<GetCashFlowQuery, CashFlowReadModel>
{
    public async Task<CashFlowReadModel> Handle(GetCashFlowQuery request, CancellationToken cancellationToken)
    {
        OwnerDashboardRequests.RequirePeriod(request.From, request.To);
        var scope = await scopeProvider.GetAsync(cancellationToken);
        var outletIds = OwnerDashboardRequests.ResolveShowrooms(scope, request.OutletId);
        var days = await store.GetCashFlowAsync(
            scope,
            outletIds,
            outletIds,
            request.OutletId is null ? scope.WarehouseIds : [],
            request.OutletId is null,
            request.From,
            request.To,
            cancellationToken);

        var points = DashboardBuckets.Build(request.From, request.To)
            .Select(bucket =>
            {
                var rows = days.Where(day => day.Date >= bucket.From && day.Date <= bucket.To).ToList();
                return new CashFlowPoint(
                    bucket.From,
                    bucket.To,
                    rows.Sum(row => row.CashReceived),
                    rows.Sum(row => row.ExpensePayments));
            })
            .ToList();
        var received = points.Sum(point => point.CashReceived);
        var paid = points.Sum(point => point.ExpensePayments);

        return new CashFlowReadModel(
            new DashboardContext(
                request.From,
                request.To,
                scope.TimeZoneId,
                OwnerDashboardRequests.DescribeShowroomScope(scope, request.OutletId),
                clock.GetUtcNow().UtcDateTime),
            DashboardBuckets.KindFor(request.From, request.To),
            points,
            received,
            paid,
            received - paid);
    }
}

public sealed record GetSalesTrendQuery(DateOnly From, DateOnly To, long? OutletId) : IQuery<SalesTrendReadModel>;

public sealed class GetSalesTrendQueryHandler(
    IOwnerDashboardScopeProvider scopeProvider,
    ISalesFinanceReadStore store,
    IOutletSalesTargetReadStore targetStore,
    TimeProvider clock)
    : IRequestHandler<GetSalesTrendQuery, SalesTrendReadModel>
{
    public async Task<SalesTrendReadModel> Handle(GetSalesTrendQuery request, CancellationToken cancellationToken)
    {
        OwnerDashboardRequests.RequirePeriod(request.From, request.To);
        var scope = await scopeProvider.GetAsync(cancellationToken);
        var outletIds = OwnerDashboardRequests.ResolveShowrooms(scope, request.OutletId);
        var facts = await store.GetSalesFactsAsync(scope, outletIds, request.From, request.To, cancellationToken);

        var schedules = await targetStore.GetSchedulesAsync(scope.BusinessId, outletIds, cancellationToken);

        var perBucket = DashboardBuckets.Build(request.From, request.To)
            .Select(bucket => (
                Bucket: bucket,
                Target: SalesTargetPerformance.Aggregate(outletIds
                    .Select(outletId => SalesTargetPerformance.ForShowroom(
                        schedules.GetValueOrDefault(outletId) ?? OutletSalesTargetSchedule.Empty(outletId),
                        bucket.From,
                        bucket.To,
                        facts.Days))
                    .ToList())))
            .ToList();
        var points = perBucket
            .Select(item => new SalesTrendPoint(
                item.Bucket.From,
                item.Bucket.To,
                facts.Days
                    .Where(day => day.BusinessDate >= item.Bucket.From && day.BusinessDate <= item.Bucket.To)
                    .Sum(day => day.NetSales),
                item.Target.Target))
            .ToList();
        var statuses = perBucket.Select(item => item.Target.Status).ToHashSet();
        var trendStatus = statuses.All(status => status == MetricStatuses.NotConfigured)
            ? MetricStatuses.NotConfigured
            : statuses.Contains(MetricStatuses.NotConfigured) || statuses.Contains(MetricStatuses.Partial)
                ? MetricStatuses.Partial
                : MetricStatuses.Ok;

        return new SalesTrendReadModel(
            new DashboardContext(
                request.From,
                request.To,
                scope.TimeZoneId,
                OwnerDashboardRequests.DescribeShowroomScope(scope, request.OutletId),
                clock.GetUtcNow().UtcDateTime),
            DashboardBuckets.KindFor(request.From, request.To),
            points,
            trendStatus);
    }
}

public sealed record GetOwnerDashboardMetaQuery : IQuery<OwnerDashboardMetaReadModel>;

public sealed class GetOwnerDashboardMetaQueryHandler(
    IOwnerDashboardScopeProvider scopeProvider,
    IInventoryDashboardReadStore inventoryStore,
    OwnerDashboardPolicy policy)
    : IRequestHandler<GetOwnerDashboardMetaQuery, OwnerDashboardMetaReadModel>
{
    public async Task<OwnerDashboardMetaReadModel> Handle(
        GetOwnerDashboardMetaQuery request,
        CancellationToken cancellationToken)
    {
        var scope = await scopeProvider.GetAsync(cancellationToken);
        return new OwnerDashboardMetaReadModel(
            scope.Today,
            scope.TimeZoneId,
            scope.Outlets,
            scope.Warehouses,
            await inventoryStore.GetCategoriesAsync(scope.BusinessId, cancellationToken),
            scope.CanCreateTransfer,
            policy,
            InventoryAnalytics.ValuationMethod);
    }
}
