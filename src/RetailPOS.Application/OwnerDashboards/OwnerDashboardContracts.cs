namespace RetailPOS.Application.OwnerDashboards;

/// <summary>
/// Classification and recommendation thresholds. The defaults are the LLD §7.3/§7.4 proposed values and need owner
/// approval (SRS D-12); they can be overridden through the <c>OwnerDashboard</c> configuration section.
/// </summary>
public sealed record OwnerDashboardPolicy
{
    public int MinEligibleSellingDays { get; init; } = 14;
    public decimal SlowVelocityPerDay { get; init; } = 0.20m;
    public decimal MinOnHandQty { get; init; } = 1;
    public decimal MinDemandVelocityPerDay { get; init; } = 0.5m;
    public int SourceExcessCoverageDays { get; init; } = 60;
    public int SourceRetainedCoverageDays { get; init; } = 30;
    public int DestinationTargetCoverageDays { get; init; } = 21;
    public decimal MinVelocityRatio { get; init; } = 2.0m;
    public decimal MinimumRetainedUnits { get; init; } = 1;
    public decimal MinRecommendationQty { get; init; } = 1;

    /// <summary>No cap unless the business approves one (LLD-D07).</summary>
    public decimal? MaxSuggestedQty { get; init; }

    public int HighPriorityCoverDays { get; init; } = 7;
    public int MediumPriorityCoverDays { get; init; } = 14;

    /// <summary>Staged-rollout switches (NFR-014).</summary>
    public bool ProfitEnabled { get; init; } = true;
    public bool TransferOpportunitiesEnabled { get; init; } = true;
}

/// <summary>
/// The reporting scope: every location of the current Business (SRS §2.1). Reads are decided by the dashboard permission
/// alone. The transfer fields only say whether the existing transfer form would accept a source; they widen nothing.
/// </summary>
public sealed record OwnerDashboardScope(
    long BusinessId,
    string TimeZoneId,
    DateOnly Today,
    IReadOnlyList<OwnerDashboardLocation> Outlets,
    IReadOnlyList<OwnerDashboardLocation> Warehouses,
    IReadOnlyList<long> TransferSourceOutletIds,
    long? DefaultSourceOutletId,
    bool CanCreateTransfer,
    bool CanTransferFromAnyLocation)
{
    public List<long> OutletIds => Outlets.Select(outlet => outlet.Id).ToList();
    public List<long> WarehouseIds => Warehouses.Select(warehouse => warehouse.Id).ToList();
}

public sealed record OwnerDashboardLocation(long Id, string Name, string Type);

public static class OwnerDashboardLocationTypes
{
    public const string Outlet = "outlet";
    public const string Warehouse = "warehouse";
}

public interface IOwnerDashboardScopeProvider
{
    Task<OwnerDashboardScope> GetAsync(CancellationToken cancellationToken);
}

/// <summary>Why a metric has, or does not have, a value. Absent values are never sent as zero.</summary>
public static class MetricStatuses
{
    public const string Ok = "ok";
    public const string Estimated = "estimated";
    public const string Partial = "partial";
    public const string Unavailable = "unavailable";
    public const string NotConfigured = "not_configured";
    public const string Disabled = "disabled";
    public const string OffDay = "off_day";
    public const string NotValued = "not_valued";
}

public sealed record MetricValue(decimal? Value, string Status, string? Note = null);

public sealed record DashboardContext(
    DateOnly From,
    DateOnly To,
    string TimeZoneId,
    string LocationScope,
    DateTime DataAsOfUtc);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);

// ---- Sales & Finance ----------------------------------------------------------------------------------------------

public sealed record SalesDayFact(
    long OutletId,
    DateOnly BusinessDate,
    decimal NetSales,
    decimal NetUnits,
    decimal Cogs,
    int MissingCostLines);

/// <param name="ReversalsWithoutEvidence">Refunds or voids whose amounts cannot be trusted; nothing is guessed for them.</param>
/// <param name="UnreversedExchanges">Replaced originals still counted as sales (no approved exchange policy, blocker B-03).</param>
public sealed record SalesFacts(
    IReadOnlyList<SalesDayFact> Days,
    int ReversalsWithoutEvidence,
    int UnreversedExchanges);

public sealed record CashFlowDayFact(DateOnly Date, decimal CashReceived, decimal ExpensePayments);

public interface ISalesFinanceReadStore
{
    Task<SalesFacts> GetSalesFactsAsync(
        OwnerDashboardScope scope,
        List<long> outletIds,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken);

    Task<decimal> GetPurchaseValueAsync(
        OwnerDashboardScope scope,
        List<long> outletIds,
        List<long> warehouseIds,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken);

    Task<decimal> GetApprovedExpenseValueAsync(
        OwnerDashboardScope scope,
        List<long> outletIds,
        List<long> warehouseIds,
        bool includeBusinessLevel,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<CashFlowDayFact>> GetCashFlowAsync(
        OwnerDashboardScope scope,
        List<long> salesOutletIds,
        List<long> expenseOutletIds,
        List<long> expenseWarehouseIds,
        bool includeBusinessLevel,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken);
}

public sealed record SalesFinanceSummaryReadModel(
    DashboardContext Context,
    MetricValue NetSales,
    decimal? NetSalesChangePercent,
    MetricValue Target,
    decimal? AchievementPercent,
    MetricValue Shortfall,
    MetricValue TargetExceeded,
    MetricValue Purchase,
    MetricValue Expense,
    MetricValue Profit,
    IReadOnlyList<string> DataQualityWarnings,
    SalesTargetCoverage TargetCoverage,
    TodayTargetReadModel Today);

/// <summary>The current business date for the selected showroom scope, whatever period the page shows.</summary>
public sealed record TodayTargetReadModel(
    DateOnly Date,
    decimal NetSales,
    MetricValue Target,
    decimal? AchievementPercent,
    MetricValue Shortfall,
    MetricValue TargetExceeded);

public sealed record ShowroomPerformanceRow(
    long OutletId,
    string OutletName,
    decimal NetSales,
    decimal? Target,
    decimal? AchievementPercent,
    decimal? Shortfall,
    decimal? TargetExceeded,
    string TargetStatus);

public sealed record ShowroomPerformanceReadModel(
    DashboardContext Context,
    IReadOnlyList<ShowroomPerformanceRow> Showrooms);

public sealed record CashFlowPoint(DateOnly From, DateOnly To, decimal CashReceived, decimal ExpensePayments);

public sealed record CashFlowReadModel(
    DashboardContext Context,
    string Bucket,
    IReadOnlyList<CashFlowPoint> Points,
    decimal TotalCashReceived,
    decimal TotalExpensePayments,
    decimal Difference);

public sealed record SalesTrendPoint(DateOnly From, DateOnly To, decimal NetSales, decimal? Target);

public sealed record SalesTrendReadModel(
    DashboardContext Context,
    string Bucket,
    IReadOnlyList<SalesTrendPoint> Points,
    string TargetStatus);

// ---- Inventory Intelligence -----------------------------------------------------------------------------------------

/// <summary>One variant at one authorized location, read in a single consistent snapshot.</summary>
public sealed record VariantLocationFact(
    long VariantId,
    long ProductId,
    string? MainCode,
    string ProductName,
    string Sku,
    string VariantLabel,
    long CategoryId,
    string CategoryName,
    bool ProductActive,
    long LocationId,
    string LocationType,
    string LocationName,
    decimal OnHand,
    decimal PendingOutbound,
    decimal PendingInbound,
    decimal NetUnitsSold,
    decimal NetSales,
    int EligibleSellingDays,
    bool HasStockEvidence,
    bool EverSold,
    DateOnly? LastSaleDate,
    decimal? UnitCost,
    decimal? StockValue)
{
    public decimal Available => Math.Max(0, OnHand);
    public bool IsShowroom => LocationType == OwnerDashboardLocationTypes.Outlet;
}

public sealed record InventorySnapshot(IReadOnlyList<VariantLocationFact> Facts, DateTime AsOfUtc);

public static class StockValuationStatuses
{
    public const string NoStock = "no_stock";
    /// <summary>Every record has a reliable positive value, possibly with some proven free stock.</summary>
    public const string Valued = "valued";
    /// <summary>All the stock is free, and documents prove it.</summary>
    public const string ZeroCost = "zero_cost";
    /// <summary>Some stock has a reliable value and some does not.</summary>
    public const string Partial = "partial";
    /// <summary>Stock exists, but none of it has a reliable value.</summary>
    public const string NotValued = "not_valued";
}

/// <summary>How much of some stock has a reliable value. Unknown cost stays out of the subtotal and is never zero.</summary>
public sealed record StockValuationCoverage(
    decimal? ValuedSubtotal,
    string Status,
    int ValuedRecords,
    decimal ValuedQuantity,
    int ZeroCostRecords,
    long ZeroCostQuantity,
    int UnvaluedRecords,
    decimal UnvaluedQuantity)
{
    public static StockValuationCoverage Of(IEnumerable<VariantLocationFact> facts)
    {
        var stocked = facts.Where(fact => fact.OnHand > 0).ToList();
        var valued = stocked.Where(fact => fact.StockValue > 0m).ToList();
        var zeroCost = stocked.Where(fact => fact.StockValue == 0m).ToList();
        var unvalued = stocked.Where(fact => fact.StockValue is null).ToList();

        var known = valued.Count + zeroCost.Count;
        var status = stocked.Count == 0
            ? StockValuationStatuses.NoStock
            : known == 0
                ? StockValuationStatuses.NotValued
                : unvalued.Count > 0
                    ? StockValuationStatuses.Partial
                    : valued.Count == 0
                        ? StockValuationStatuses.ZeroCost
                        : StockValuationStatuses.Valued;

        return new StockValuationCoverage(
            known == 0 ? null : valued.Sum(fact => fact.StockValue!.Value),
            status,
            valued.Count,
            valued.Sum(fact => (long)fact.OnHand),
            zeroCost.Count,
            zeroCost.Sum(fact => (long)fact.OnHand),
            unvalued.Count,
            unvalued.Sum(fact => (long)fact.OnHand));
    }

    public string MetricStatus => Status switch
    {
        StockValuationStatuses.Partial => MetricStatuses.Partial,
        StockValuationStatuses.NotValued => MetricStatuses.NotValued,
        _ => MetricStatuses.Ok
    };
}

public sealed record CategoryOption(long Id, string Name);

public sealed record BusinessLocations(
    IReadOnlyList<OwnerDashboardLocation> Outlets,
    IReadOnlyList<OwnerDashboardLocation> Warehouses);

public interface IInventoryDashboardReadStore
{
    /// <param name="includeCost">False skips the cost replay when a screen does not show cost; purely an efficiency choice.</param>
    Task<InventorySnapshot> GetSnapshotAsync(
        OwnerDashboardScope scope,
        DateOnly from,
        DateOnly to,
        bool includeCost,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<CategoryOption>> GetCategoriesAsync(long businessId, CancellationToken cancellationToken);

    /// <summary>Every location of the Business: the reporting scope of both dashboards.</summary>
    Task<BusinessLocations> GetBusinessLocationsAsync(long businessId, CancellationToken cancellationToken);
}

public sealed record InventorySummaryReadModel(
    DashboardContext Context,
    MetricValue TotalStockValue,
    long TotalStockQuantity,
    int ActiveStockedProducts,
    decimal StockedVariants,
    MetricValue SlowMovingStockValue,
    decimal? SlowMovingSharePercent,
    decimal OutOfStockVariants,
    int? TransferOpportunities,
    int? HighPriorityTransferOpportunities,
    string TransferOpportunityStatus,
    int NegativeStockRows,
    int UnvaluedStockRows,
    string ValuationMethod,
    IReadOnlyList<string> DataQualityWarnings,
    StockValuationCoverage Valuation,
    StockValuationCoverage SlowMovingValuation);

public sealed record StockValueByLocationRow(
    long LocationId,
    string LocationType,
    string LocationName,
    long Quantity,
    decimal? StockValue,
    decimal? PercentOfTotal,
    StockValuationCoverage Valuation);

public sealed record StockValueByLocationReadModel(
    DashboardContext Context,
    IReadOnlyList<StockValueByLocationRow> Locations,
    decimal? TotalStockValue,
    string ValueStatus,
    StockValuationCoverage Valuation);

public sealed record BestSellerRow(
    int Rank,
    long ProductId,
    string? MainCode,
    string ProductName,
    long VariantId,
    string Sku,
    string VariantLabel,
    string CategoryName,
    decimal NetUnitsSold,
    decimal NetSales,
    long CurrentAvailableStock);

public static class SlowClassifications
{
    public const string ZeroSales = "zero_sales";
    public const string NeverSold = "never_sold";
    public const string LowSales = "low_sales";
    public const string Normal = "normal";
    public const string InsufficientEvidence = "insufficient_evidence";

    public static bool IsSlow(string classification) =>
        classification is ZeroSales or NeverSold or LowSales;
}

public sealed record SlowClassificationResult(string Classification, decimal? VelocityPerDay, string Reason);

public sealed record SlowMoverRow(
    long ProductId,
    string? MainCode,
    string ProductName,
    long VariantId,
    string Sku,
    string VariantLabel,
    string CategoryName,
    long LocationId,
    string LocationName,
    decimal AvailableStock,
    decimal NetUnitsSold,
    int? DaysSinceLastSale,
    decimal? StockValue,
    int EligibleSellingDays,
    decimal? VelocityPerDay,
    string Classification,
    string Reason);

public sealed record SlowMoversReadModel(
    DashboardContext Context,
    PagedResult<SlowMoverRow> Rows,
    int InsufficientEvidenceCount);

public static class TransferPriorities
{
    public const string High = "high";
    public const string Medium = "medium";
    public const string Low = "low";
}

public static class TransferReasonCodes
{
    public const string SourceExcess = "source_excess";
    public const string SourceNoSales = "source_no_sales";
    public const string DestinationLowCover = "destination_low_cover";
    public const string DestinationOutOfStock = "destination_out_of_stock";
    public const string VelocityGap = "velocity_gap";
    public const string LimitedBySourceSpare = "limited_by_source_spare";
    public const string LimitedByPolicyCap = "limited_by_policy_cap";
    public const string ReducedByInbound = "reduced_by_inbound";
    public const string ReducedByPendingOutbound = "reduced_by_pending_outbound";
}

public sealed record TransferSideDetail(
    long LocationId,
    string LocationName,
    decimal Available,
    decimal NetUnitsSold,
    int EligibleSellingDays,
    decimal VelocityPerDay,
    decimal? CoverageDays,
    decimal PendingOutbound,
    decimal PendingInbound);

public sealed record TransferOpportunity(
    string RecommendationKey,
    long ProductId,
    string? MainCode,
    string ProductName,
    long VariantId,
    string Sku,
    string VariantLabel,
    long CategoryId,
    string CategoryName,
    TransferSideDetail Source,
    TransferSideDetail Destination,
    decimal SourceProtectedQty,
    decimal SourceSpareQty,
    decimal DestinationNeedQty,
    decimal SuggestedQty,
    string Priority,
    IReadOnlyList<string> ReasonCodes,
    bool CanInitiate);

public sealed record TransferOpportunitiesReadModel(
    DashboardContext Context,
    PagedResult<TransferOpportunity> Rows,
    string Status,
    DateTime StockAsOfUtc);

public sealed record ProductInsightLocationRow(
    long LocationId,
    string LocationType,
    string LocationName,
    long VariantId,
    string Sku,
    string VariantLabel,
    decimal AvailableStock,
    decimal NetUnitsSold,
    decimal NetSales,
    decimal? StockValue,
    int? DaysSinceLastSale);

public sealed record ProductInsightsReadModel(
    DashboardContext Context,
    long ProductId,
    string? MainCode,
    string ProductName,
    string CategoryName,
    long TotalAvailableStock,
    decimal TotalNetUnitsSold,
    decimal TotalNetSales,
    decimal? TotalStockValue,
    IReadOnlyList<ProductInsightLocationRow> Locations,
    StockValuationCoverage Valuation);

public sealed record OwnerDashboardMetaReadModel(
    DateOnly Today,
    string TimeZoneId,
    IReadOnlyList<OwnerDashboardLocation> Showrooms,
    IReadOnlyList<OwnerDashboardLocation> Warehouses,
    IReadOnlyList<CategoryOption> Categories,
    bool CanCreateTransfer,
    OwnerDashboardPolicy Thresholds,
    string ValuationMethod);
