namespace RetailPOS.Application.AccountingReports.ReadModels;

public sealed record ProfitabilityFilter(
    DateTime? DateFrom,
    DateTime? DateTo,
    DateTime? ComparisonDateFrom,
    DateTime? ComparisonDateTo,
    long? LocationId,
    string? LocationType,
    long? ProductId = null,
    long? CategoryId = null);

public sealed record BusinessProfitabilityReadModel(
    DateTime DateFrom,
    DateTime DateTo,
    decimal Revenue,
    decimal CostOfGoodsSold,
    decimal Expenses,
    decimal NetProfit,
    decimal AuthoritativePnlNetProfit,
    decimal ReconciliationDifference);

public sealed record OutletProfitabilityReadModel(
    DateTime DateFrom,
    DateTime DateTo,
    IReadOnlyList<OutletProfitabilityRowReadModel> Outlets,
    OutletProfitabilityRowReadModel Unallocated,
    decimal BusinessRevenue,
    decimal BusinessCostOfGoodsSold,
    decimal BusinessExpenses,
    decimal BusinessNetProfit,
    decimal AttributedRevenue,
    decimal AttributedCostOfGoodsSold,
    decimal AttributedExpenses,
    decimal AttributedNetProfit,
    decimal ReconciliationDifference);

public sealed record OutletProfitabilityRowReadModel(
    long? OutletId,
    string OutletName,
    decimal Revenue,
    decimal CostOfGoodsSold,
    decimal Expenses,
    decimal NetProfit,
    string? UnallocatedReason);

public sealed record ComparativeBusinessProfitabilityReadModel(
    BusinessProfitabilityReadModel Current,
    BusinessProfitabilityReadModel Comparison);

public sealed record ComparativeOutletProfitabilityReadModel(
    OutletProfitabilityReadModel Current,
    OutletProfitabilityReadModel Comparison);

public sealed record ProductContributionReadModel(
    DateTime DateFrom,
    DateTime DateTo,
    string ContributionBasis,
    IReadOnlyList<ProductContributionRowReadModel> Products,
    IReadOnlyList<CategoryContributionRowReadModel> Categories,
    decimal AttributedRevenue,
    decimal AttributedCostOfGoodsSold,
    decimal AttributedContribution,
    decimal AuthoritativeRevenue,
    decimal AuthoritativeCostOfGoodsSold,
    decimal UnallocatedRevenue,
    decimal UnallocatedCostOfGoodsSold,
    decimal RevenueReconciliationDifference,
    decimal CogsReconciliationDifference,
    string HistoricalLimitation);

public sealed record ProductContributionRowReadModel(
    long ProductId,
    string ProductName,
    string? ProductCode,
    long CategoryId,
    string CategoryName,
    decimal Revenue,
    decimal CostOfGoodsSold,
    decimal Contribution,
    decimal MarginPercent);

public sealed record CategoryContributionRowReadModel(
    long CategoryId,
    string CategoryName,
    decimal Revenue,
    decimal CostOfGoodsSold,
    decimal Contribution,
    decimal MarginPercent);

public sealed record ComparativeProductContributionReadModel(
    ProductContributionReadModel Current,
    ProductContributionReadModel Comparison);
