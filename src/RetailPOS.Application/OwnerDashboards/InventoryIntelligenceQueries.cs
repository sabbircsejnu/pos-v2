using MediatR;
using RetailPOS.Application.Abstractions.Messaging;

namespace RetailPOS.Application.OwnerDashboards;

/// <summary>Location, category and search filter of the Inventory Intelligence screen.</summary>
public sealed record InventoryFilter(
    DateOnly From,
    DateOnly To,
    long? LocationId,
    string? LocationType,
    long? CategoryId,
    string? Search);

public static class InventoryAnalytics
{
    public const string ValuationMethod = "Weighted average cost per variant and location (stock ledger replay)";

    public static void Validate(InventoryFilter filter, OwnerDashboardScope scope)
    {
        OwnerDashboardRequests.RequirePeriod(filter.From, filter.To);
        OwnerDashboardRequests.NormalizeSearch(filter.Search);
        if (filter.LocationId is null)
            return;

        var allowed = filter.LocationType?.Trim().ToLowerInvariant() switch
        {
            OwnerDashboardLocationTypes.Outlet => scope.Outlets,
            OwnerDashboardLocationTypes.Warehouse => scope.Warehouses,
            _ => throw new ArgumentException("A location filter needs a location type of outlet or warehouse.")
        };
        if (!allowed.Any(location => location.Id == filter.LocationId))
            throw new UnauthorizedAccessException("The selected location does not belong to this Business.");
    }

    public static string DescribeScope(InventoryFilter filter, OwnerDashboardScope scope)
    {
        if (filter.LocationId is null)
            return "all-locations";

        var locations = string.Equals(filter.LocationType, OwnerDashboardLocationTypes.Outlet, StringComparison.OrdinalIgnoreCase)
            ? scope.Outlets
            : scope.Warehouses;
        return locations.First(location => location.Id == filter.LocationId).Name;
    }

    public static bool AtLocation(VariantLocationFact fact, InventoryFilter filter) =>
        filter.LocationId is null
        || (fact.LocationId == filter.LocationId
            && string.Equals(fact.LocationType, filter.LocationType, StringComparison.OrdinalIgnoreCase));

    public static bool MatchesProduct(VariantLocationFact fact, InventoryFilter filter)
    {
        if (filter.CategoryId is not null && fact.CategoryId != filter.CategoryId)
            return false;

        var search = OwnerDashboardRequests.NormalizeSearch(filter.Search);
        return search is null
            || Contains(fact.MainCode, search)
            || Contains(fact.ProductName, search)
            || Contains(fact.Sku, search);
    }

    public static bool Contains(string? value, string search) =>
        value?.Contains(search, StringComparison.OrdinalIgnoreCase) == true;

    public static int? DaysSince(DateOnly? lastSale, DateOnly today) =>
        lastSale is null ? null : today.DayNumber - lastSale.Value.DayNumber;

    public static DashboardContext Context(InventoryFilter filter, OwnerDashboardScope scope, DateTime asOfUtc) =>
        new(filter.From, filter.To, scope.TimeZoneId, DescribeScope(filter, scope), asOfUtc);

    public static PagedResult<T> Page<T>(IReadOnlyList<T> rows, int page, int pageSize) =>
        new(rows.Skip((page - 1) * pageSize).Take(pageSize).ToList(), rows.Count, page, pageSize);

    /// <summary>Whether the caller's existing transfer form would accept this source (the API re-checks on submit).</summary>
    public static bool CanInitiateFrom(OwnerDashboardScope scope, long sourceOutletId) =>
        scope.CanCreateTransfer
        && scope.TransferSourceOutletIds.Contains(sourceOutletId)
        && (scope.CanTransferFromAnyLocation || scope.DefaultSourceOutletId == sourceOutletId);

    public static IReadOnlyList<TransferOpportunity> BuildOpportunities(
        InventorySnapshot snapshot,
        OwnerDashboardScope scope,
        OwnerDashboardPolicy policy) =>
        TransferOpportunityEngine.Build(
            snapshot.Facts,
            policy,
            source => CanInitiateFrom(scope, source.LocationId));
}

public sealed record GetInventorySummaryQuery(InventoryFilter Filter) : IQuery<InventorySummaryReadModel>;

public sealed class GetInventorySummaryQueryHandler(
    IOwnerDashboardScopeProvider scopeProvider,
    IInventoryDashboardReadStore store,
    OwnerDashboardPolicy policy)
    : IRequestHandler<GetInventorySummaryQuery, InventorySummaryReadModel>
{
    public async Task<InventorySummaryReadModel> Handle(
        GetInventorySummaryQuery request,
        CancellationToken cancellationToken)
    {
        var filter = request.Filter;
        var scope = await scopeProvider.GetAsync(cancellationToken);
        InventoryAnalytics.Validate(filter, scope);

        var snapshot = await store.GetSnapshotAsync(scope, filter.From, filter.To, true, cancellationToken);
        var selected = snapshot.Facts
            .Where(fact => InventoryAnalytics.AtLocation(fact, filter) && InventoryAnalytics.MatchesProduct(fact, filter))
            .ToList();
        var stocked = selected.Where(fact => fact.OnHand > 0).ToList();
        var negativeRows = selected.Count(fact => fact.OnHand < 0);
        var valuation = StockValuationCoverage.Of(stocked);
        var slowValuation = StockValuationCoverage.Of(selected
            .Where(fact => SlowMoverClassifier.IsEligible(fact, policy)
                && SlowClassifications.IsSlow(SlowMoverClassifier.Classify(fact, policy).Classification)));

        var opportunities = policy.TransferOpportunitiesEnabled
            ? InventoryAnalytics.BuildOpportunities(snapshot, scope, policy)
                .Where(row => MatchesLocation(row, filter) && MatchesProduct(row, filter))
                .ToList()
            : null;

        var warnings = new List<string>();
        if (negativeRows > 0)
            warnings.Add($"{negativeRows} stock record(s) are negative. They are excluded from quantity and value and need review.");
        if (valuation.UnvaluedRecords > 0)
            warnings.Add($"{valuation.UnvaluedRecords} stock record(s) holding {valuation.UnvaluedQuantity} unit(s) have no reliable cost and are excluded from stock value.");

        return new InventorySummaryReadModel(
            InventoryAnalytics.Context(filter, scope, snapshot.AsOfUtc),
            new MetricValue(valuation.ValuedSubtotal, valuation.MetricStatus, InventoryAnalytics.ValuationMethod),
            stocked.Sum(fact => (long)fact.OnHand),
            stocked.Select(fact => fact.ProductId).Distinct().Count(),
            stocked.Select(fact => fact.VariantId).Distinct().Count(),
            new MetricValue(
                slowValuation.ValuedSubtotal,
                slowValuation.MetricStatus,
                "Cost value of showroom stock classified as zero or low sales in the analysis period."),
            // A share of a partly known total, or of a partly known slow value, would be a guess.
            valuation.UnvaluedRecords == 0 && slowValuation.UnvaluedRecords == 0 && valuation.ValuedSubtotal > 0m
                ? Math.Round(100m * (slowValuation.ValuedSubtotal ?? 0m) / valuation.ValuedSubtotal.Value, 2, MidpointRounding.AwayFromZero)
                : null,
            selected.Count(fact => fact.IsShowroom && fact.ProductActive && fact.Available <= 0),
            opportunities?.Count,
            opportunities?.Count(row => row.Priority == TransferPriorities.High),
            policy.TransferOpportunitiesEnabled ? MetricStatuses.Ok : MetricStatuses.Disabled,
            negativeRows,
            valuation.UnvaluedRecords,
            InventoryAnalytics.ValuationMethod,
            warnings,
            valuation,
            slowValuation);
    }

    private static bool MatchesLocation(TransferOpportunity row, InventoryFilter filter) =>
        filter.LocationId is null
        || (string.Equals(filter.LocationType, OwnerDashboardLocationTypes.Outlet, StringComparison.OrdinalIgnoreCase)
            && (row.Source.LocationId == filter.LocationId || row.Destination.LocationId == filter.LocationId));

    private static bool MatchesProduct(TransferOpportunity row, InventoryFilter filter)
    {
        if (filter.CategoryId is not null && row.CategoryId != filter.CategoryId)
            return false;

        var search = OwnerDashboardRequests.NormalizeSearch(filter.Search);
        return search is null
            || InventoryAnalytics.Contains(row.MainCode, search)
            || InventoryAnalytics.Contains(row.ProductName, search)
            || InventoryAnalytics.Contains(row.Sku, search);
    }
}

public sealed record GetStockValueByLocationQuery(InventoryFilter Filter) : IQuery<StockValueByLocationReadModel>;

public sealed class GetStockValueByLocationQueryHandler(
    IOwnerDashboardScopeProvider scopeProvider,
    IInventoryDashboardReadStore store)
    : IRequestHandler<GetStockValueByLocationQuery, StockValueByLocationReadModel>
{
    public async Task<StockValueByLocationReadModel> Handle(
        GetStockValueByLocationQuery request,
        CancellationToken cancellationToken)
    {
        var filter = request.Filter;
        var scope = await scopeProvider.GetAsync(cancellationToken);
        InventoryAnalytics.Validate(filter, scope);
        var snapshot = await store.GetSnapshotAsync(scope, filter.From, filter.To, true, cancellationToken);

        var selected = snapshot.Facts
            .Where(fact => fact.OnHand > 0
                && InventoryAnalytics.AtLocation(fact, filter)
                && InventoryAnalytics.MatchesProduct(fact, filter))
            .ToList();
        var valuation = StockValuationCoverage.Of(selected);
        var total = valuation.ValuedSubtotal;

        // Every location is listed, including ones holding nothing, and only the sum itself is rounded for
        // display: the per-location subtotals add up to the valued subtotal on the summary card exactly.
        var rows = scope.Outlets.Concat(scope.Warehouses)
            .Where(location => filter.LocationId is null
                || (location.Id == filter.LocationId
                    && string.Equals(location.Type, filter.LocationType, StringComparison.OrdinalIgnoreCase)))
            .Select(location =>
            {
                var facts = selected
                    .Where(fact => fact.LocationId == location.Id && fact.LocationType == location.Type)
                    .ToList();
                var coverage = StockValuationCoverage.Of(facts);
                var value = coverage.ValuedSubtotal;
                return new StockValueByLocationRow(
                    location.Id,
                    location.Type,
                    location.Name,
                    facts.Sum(fact => (long)fact.OnHand),
                    value,
                    total > 0m && value is not null
                        ? Math.Round(100m * value.Value / total.Value, 2, MidpointRounding.AwayFromZero)
                        : null,
                    coverage);
            })
            .OrderBy(row => row.StockValue is null)
            .ThenByDescending(row => row.StockValue ?? 0m)
            .ThenByDescending(row => row.Quantity)
            .ThenBy(row => row.LocationName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new StockValueByLocationReadModel(
            InventoryAnalytics.Context(filter, scope, snapshot.AsOfUtc),
            rows,
            total,
            valuation.MetricStatus,
            valuation);
    }
}

public sealed record GetBestSellersQuery(
    InventoryFilter Filter,
    string? SortBy,
    string? SortDirection,
    int Page,
    int PageSize)
    : IQuery<PagedResult<BestSellerRow>>;

public sealed class GetBestSellersQueryHandler(
    IOwnerDashboardScopeProvider scopeProvider,
    IInventoryDashboardReadStore store)
    : IRequestHandler<GetBestSellersQuery, PagedResult<BestSellerRow>>
{
    public async Task<PagedResult<BestSellerRow>> Handle(
        GetBestSellersQuery request,
        CancellationToken cancellationToken)
    {
        var (page, pageSize) = OwnerDashboardRequests.RequirePaging(request.Page, request.PageSize);
        var filter = request.Filter;
        var scope = await scopeProvider.GetAsync(cancellationToken);
        InventoryAnalytics.Validate(filter, scope);
        var snapshot = await store.GetSnapshotAsync(scope, filter.From, filter.To, false, cancellationToken);

        var ranked = snapshot.Facts
            .Where(fact => InventoryAnalytics.AtLocation(fact, filter) && InventoryAnalytics.MatchesProduct(fact, filter))
            .GroupBy(fact => fact.VariantId)
            .Select(group => new
            {
                First = group.First(),
                Units = group.Sum(fact => fact.NetUnitsSold),
                Net = group.Sum(fact => fact.NetSales),
                Available = group.Sum(fact => (long)fact.Available)
            })
            .Where(row => row.Units > 0)
            .OrderByDescending(row => row.Units)
            .ThenByDescending(row => row.Net)
            .ThenBy(row => row.First.MainCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.First.Sku, StringComparer.OrdinalIgnoreCase)
            .Select((row, index) => new BestSellerRow(
                index + 1,
                row.First.ProductId,
                row.First.MainCode,
                row.First.ProductName,
                row.First.VariantId,
                row.First.Sku,
                row.First.VariantLabel,
                row.First.CategoryName,
                row.Units,
                row.Net,
                row.Available))
            .ToList();

        return InventoryAnalytics.Page(Sort(ranked, request.SortBy, request.SortDirection), page, pageSize);
    }

    /// <summary>Allow-listed sort keys. The rank stays the default ranking and the tie-break keeps pages stable.</summary>
    private static List<BestSellerRow> Sort(List<BestSellerRow> rows, string? sortBy, string? direction)
    {
        var descending = string.Equals(direction, "desc", StringComparison.OrdinalIgnoreCase);
        var ordered = sortBy?.ToLowerInvariant() switch
        {
            null or "" or "rank" or "netunitssold" when string.IsNullOrEmpty(direction) => rows.OrderBy(row => row.Rank),
            null or "" or "rank" => descending ? rows.OrderByDescending(row => row.Rank) : rows.OrderBy(row => row.Rank),
            "netunitssold" => descending ? rows.OrderByDescending(row => row.NetUnitsSold) : rows.OrderBy(row => row.NetUnitsSold),
            "netsales" => descending ? rows.OrderByDescending(row => row.NetSales) : rows.OrderBy(row => row.NetSales),
            "currentavailablestock" => descending ? rows.OrderByDescending(row => row.CurrentAvailableStock) : rows.OrderBy(row => row.CurrentAvailableStock),
            "maincode" => descending ? rows.OrderByDescending(row => row.MainCode, StringComparer.OrdinalIgnoreCase) : rows.OrderBy(row => row.MainCode, StringComparer.OrdinalIgnoreCase),
            "productname" => descending ? rows.OrderByDescending(row => row.ProductName, StringComparer.OrdinalIgnoreCase) : rows.OrderBy(row => row.ProductName, StringComparer.OrdinalIgnoreCase),
            "sku" => descending ? rows.OrderByDescending(row => row.Sku, StringComparer.OrdinalIgnoreCase) : rows.OrderBy(row => row.Sku, StringComparer.OrdinalIgnoreCase),
            "categoryname" => descending ? rows.OrderByDescending(row => row.CategoryName, StringComparer.OrdinalIgnoreCase) : rows.OrderBy(row => row.CategoryName, StringComparer.OrdinalIgnoreCase),
            _ => throw new ArgumentException("Unsupported sort column.")
        };
        return ordered.ThenBy(row => row.Rank).ToList();
    }
}

public sealed record GetSlowMoversQuery(
    InventoryFilter Filter,
    string? Classification,
    int? MinStock,
    int? MaxStock,
    string? SortBy,
    string? SortDirection,
    int Page,
    int PageSize)
    : IQuery<SlowMoversReadModel>;

public sealed class GetSlowMoversQueryHandler(
    IOwnerDashboardScopeProvider scopeProvider,
    IInventoryDashboardReadStore store,
    OwnerDashboardPolicy policy)
    : IRequestHandler<GetSlowMoversQuery, SlowMoversReadModel>
{
    public async Task<SlowMoversReadModel> Handle(GetSlowMoversQuery request, CancellationToken cancellationToken)
    {
        var (page, pageSize) = OwnerDashboardRequests.RequirePaging(request.Page, request.PageSize);
        var filter = request.Filter;
        var scope = await scopeProvider.GetAsync(cancellationToken);
        InventoryAnalytics.Validate(filter, scope);
        if (request.Classification is not (null or "" or "all" or SlowClassifications.ZeroSales or SlowClassifications.LowSales))
            throw new ArgumentException("Unsupported classification filter.");
        if (request.MinStock > request.MaxStock)
            throw new ArgumentException("Minimum stock cannot exceed maximum stock.");

        var snapshot = await store.GetSnapshotAsync(scope, filter.From, filter.To, true, cancellationToken);
        var classified = snapshot.Facts
            .Where(fact => InventoryAnalytics.AtLocation(fact, filter)
                && InventoryAnalytics.MatchesProduct(fact, filter)
                && SlowMoverClassifier.IsEligible(fact, policy))
            .Select(fact => new { Fact = fact, Result = SlowMoverClassifier.Classify(fact, policy) })
            .ToList();

        var rows = classified
            .Where(row => SlowClassifications.IsSlow(row.Result.Classification))
            .Where(row => request.Classification switch
            {
                SlowClassifications.ZeroSales => row.Result.Classification
                    is SlowClassifications.ZeroSales or SlowClassifications.NeverSold,
                SlowClassifications.LowSales => row.Result.Classification == SlowClassifications.LowSales,
                _ => true
            })
            .Where(row => (request.MinStock is null || row.Fact.Available >= request.MinStock)
                && (request.MaxStock is null || row.Fact.Available <= request.MaxStock))
            .Select(row => new SlowMoverRow(
                row.Fact.ProductId,
                row.Fact.MainCode,
                row.Fact.ProductName,
                row.Fact.VariantId,
                row.Fact.Sku,
                row.Fact.VariantLabel,
                row.Fact.CategoryName,
                row.Fact.LocationId,
                row.Fact.LocationName,
                row.Fact.Available,
                row.Fact.NetUnitsSold,
                InventoryAnalytics.DaysSince(row.Fact.LastSaleDate, scope.Today),
                row.Fact.StockValue,
                row.Fact.EligibleSellingDays,
                row.Result.VelocityPerDay,
                row.Result.Classification,
                row.Result.Reason))
            .ToList();

        return new SlowMoversReadModel(
            InventoryAnalytics.Context(filter, scope, snapshot.AsOfUtc),
            InventoryAnalytics.Page(Sort(rows, request.SortBy, request.SortDirection), page, pageSize),
            classified.Count(row => row.Result.Classification == SlowClassifications.InsufficientEvidence));
    }

    private static List<SlowMoverRow> Sort(List<SlowMoverRow> rows, string? sortBy, string? direction)
    {
        var ascending = string.Equals(direction, "asc", StringComparison.OrdinalIgnoreCase);
        IOrderedEnumerable<SlowMoverRow> ordered = sortBy?.ToLowerInvariant() switch
        {
            // Unknown value is not the lowest value, so it sorts last in both directions.
            null or "" or "stockvalue" => ascending
                ? rows.OrderBy(row => row.StockValue is null).ThenBy(row => row.StockValue)
                : rows.OrderBy(row => row.StockValue is null).ThenByDescending(row => row.StockValue),
            "availablestock" => ascending
                ? rows.OrderBy(row => row.AvailableStock)
                : rows.OrderByDescending(row => row.AvailableStock),
            "netunitssold" => ascending
                ? rows.OrderBy(row => row.NetUnitsSold)
                : rows.OrderByDescending(row => row.NetUnitsSold),
            "dayssincelastsale" => ascending
                ? rows.OrderBy(row => row.DaysSinceLastSale ?? int.MaxValue)
                : rows.OrderByDescending(row => row.DaysSinceLastSale ?? int.MaxValue),
            "maincode" => ascending
                ? rows.OrderBy(row => row.MainCode, StringComparer.OrdinalIgnoreCase)
                : rows.OrderByDescending(row => row.MainCode, StringComparer.OrdinalIgnoreCase),
            "productname" => ascending
                ? rows.OrderBy(row => row.ProductName, StringComparer.OrdinalIgnoreCase)
                : rows.OrderByDescending(row => row.ProductName, StringComparer.OrdinalIgnoreCase),
            "locationname" => ascending
                ? rows.OrderBy(row => row.LocationName, StringComparer.OrdinalIgnoreCase)
                : rows.OrderByDescending(row => row.LocationName, StringComparer.OrdinalIgnoreCase),
            _ => throw new ArgumentException("Unsupported sort column.")
        };
        return ordered.ThenBy(row => row.VariantId).ThenBy(row => row.LocationId).ToList();
    }
}

public sealed record GetTransferOpportunitiesQuery(
    InventoryFilter Filter,
    long? SourceLocationId,
    long? DestinationLocationId,
    string? Priority,
    int Page,
    int PageSize)
    : IQuery<TransferOpportunitiesReadModel>;

public sealed class GetTransferOpportunitiesQueryHandler(
    IOwnerDashboardScopeProvider scopeProvider,
    IInventoryDashboardReadStore store,
    OwnerDashboardPolicy policy)
    : IRequestHandler<GetTransferOpportunitiesQuery, TransferOpportunitiesReadModel>
{
    public async Task<TransferOpportunitiesReadModel> Handle(
        GetTransferOpportunitiesQuery request,
        CancellationToken cancellationToken)
    {
        var (page, pageSize) = OwnerDashboardRequests.RequirePaging(request.Page, request.PageSize);
        var filter = request.Filter;
        var scope = await scopeProvider.GetAsync(cancellationToken);
        InventoryAnalytics.Validate(filter, scope);
        if (request.Priority is not (null or "" or TransferPriorities.High or TransferPriorities.Medium or TransferPriorities.Low))
            throw new ArgumentException("Unsupported priority filter.");
        RequireShowroom(scope, request.SourceLocationId);
        RequireShowroom(scope, request.DestinationLocationId);

        var context = InventoryAnalytics.Context(filter, scope, DateTime.UtcNow);
        if (!policy.TransferOpportunitiesEnabled)
            return new TransferOpportunitiesReadModel(
                context,
                new PagedResult<TransferOpportunity>([], 0, page, pageSize),
                MetricStatuses.Disabled,
                context.DataAsOfUtc);

        // Allocation runs across every authorized showroom first, so filters only narrow the view and never change a
        // suggested quantity. Only showrooms of the same Business are ever compared (AC-19).
        var snapshot = await store.GetSnapshotAsync(scope, filter.From, filter.To, false, cancellationToken);
        var rows = InventoryAnalytics.BuildOpportunities(snapshot, scope, policy)
            .Where(row => filter.LocationId is null
                || (string.Equals(filter.LocationType, OwnerDashboardLocationTypes.Outlet, StringComparison.OrdinalIgnoreCase)
                    && (row.Source.LocationId == filter.LocationId || row.Destination.LocationId == filter.LocationId)))
            .Where(row => request.SourceLocationId is null || row.Source.LocationId == request.SourceLocationId)
            .Where(row => request.DestinationLocationId is null || row.Destination.LocationId == request.DestinationLocationId)
            .Where(row => string.IsNullOrEmpty(request.Priority) || row.Priority == request.Priority)
            .Where(row => filter.CategoryId is null || row.CategoryId == filter.CategoryId)
            .Where(row =>
            {
                var search = OwnerDashboardRequests.NormalizeSearch(filter.Search);
                return search is null
                    || InventoryAnalytics.Contains(row.MainCode, search)
                    || InventoryAnalytics.Contains(row.ProductName, search)
                    || InventoryAnalytics.Contains(row.Sku, search);
            })
            .ToList();

        return new TransferOpportunitiesReadModel(
            InventoryAnalytics.Context(filter, scope, snapshot.AsOfUtc),
            InventoryAnalytics.Page(rows, page, pageSize),
            MetricStatuses.Ok,
            snapshot.AsOfUtc);
    }

    private static void RequireShowroom(OwnerDashboardScope scope, long? locationId)
    {
        if (locationId is not null && !scope.Outlets.Any(outlet => outlet.Id == locationId))
            throw new UnauthorizedAccessException("The selected showroom does not belong to this Business.");
    }
}

public sealed record GetTransferOpportunityDetailQuery(string RecommendationKey, DateOnly From, DateOnly To)
    : IQuery<TransferOpportunity>;

public sealed class GetTransferOpportunityDetailQueryHandler(
    IOwnerDashboardScopeProvider scopeProvider,
    IInventoryDashboardReadStore store,
    OwnerDashboardPolicy policy)
    : IRequestHandler<GetTransferOpportunityDetailQuery, TransferOpportunity>
{
    public async Task<TransferOpportunity> Handle(
        GetTransferOpportunityDetailQuery request,
        CancellationToken cancellationToken)
    {
        OwnerDashboardRequests.RequirePeriod(request.From, request.To);
        var scope = await scopeProvider.GetAsync(cancellationToken);
        if (!policy.TransferOpportunitiesEnabled)
            throw new KeyNotFoundException("This recommendation is no longer available.");

        // Recomputed from live data: a key names a pair, it grants nothing, and it only resolves inside this Business.
        var snapshot = await store.GetSnapshotAsync(scope, request.From, request.To, false, cancellationToken);
        return InventoryAnalytics.BuildOpportunities(snapshot, scope, policy)
                .FirstOrDefault(row => row.RecommendationKey == request.RecommendationKey)
            ?? throw new KeyNotFoundException("This recommendation is no longer available.");
    }
}

public sealed record GetProductInsightsQuery(long ProductId, long? VariantId, InventoryFilter Filter)
    : IQuery<ProductInsightsReadModel>;

public sealed class GetProductInsightsQueryHandler(
    IOwnerDashboardScopeProvider scopeProvider,
    IInventoryDashboardReadStore store)
    : IRequestHandler<GetProductInsightsQuery, ProductInsightsReadModel>
{
    public async Task<ProductInsightsReadModel> Handle(
        GetProductInsightsQuery request,
        CancellationToken cancellationToken)
    {
        var filter = request.Filter;
        var scope = await scopeProvider.GetAsync(cancellationToken);
        InventoryAnalytics.Validate(filter, scope);
        var snapshot = await store.GetSnapshotAsync(scope, filter.From, filter.To, true, cancellationToken);

        var facts = snapshot.Facts
            .Where(fact => fact.ProductId == request.ProductId
                && (request.VariantId is null || fact.VariantId == request.VariantId)
                && InventoryAnalytics.AtLocation(fact, filter))
            .ToList();
        if (facts.Count == 0)
            throw new KeyNotFoundException("Product not found in this Business.");

        var first = facts[0];
        var valuation = StockValuationCoverage.Of(facts);
        return new ProductInsightsReadModel(
            InventoryAnalytics.Context(filter, scope, snapshot.AsOfUtc),
            first.ProductId,
            first.MainCode,
            first.ProductName,
            first.CategoryName,
            facts.Sum(fact => (long)fact.Available),
            facts.Sum(fact => fact.NetUnitsSold),
            facts.Sum(fact => fact.NetSales),
            valuation.ValuedSubtotal,
            facts
                .OrderBy(fact => fact.LocationName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(fact => fact.Sku, StringComparer.OrdinalIgnoreCase)
                .Select(fact => new ProductInsightLocationRow(
                    fact.LocationId,
                    fact.LocationType,
                    fact.LocationName,
                    fact.VariantId,
                    fact.Sku,
                    fact.VariantLabel,
                    fact.Available,
                    fact.NetUnitsSold,
                    fact.NetSales,
                    fact.StockValue,
                    InventoryAnalytics.DaysSince(fact.LastSaleDate, scope.Today)))
                .ToList(),
            valuation);
    }
}
