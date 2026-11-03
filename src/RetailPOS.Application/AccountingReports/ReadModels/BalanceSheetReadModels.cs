namespace RetailPOS.Application.AccountingReports.ReadModels;

public sealed record BalanceSheetReportFilter(
    DateTime? AsOfDate,
    long? LocationId,
    string? LocationType);

public sealed record BalanceSheetReportReadModel(
    DateTime AsOfDate,
    bool IsManagementView,
    string? CutoverWarning,
    BalanceSheetSectionReadModel Assets,
    BalanceSheetSectionReadModel Liabilities,
    BalanceSheetSectionReadModel Equity,
    decimal CurrentYearEarnings,
    decimal UnclosedPriorYearEarnings,
    decimal TotalAssets,
    decimal TotalLiabilitiesAndEquity,
    decimal ReconciliationDifference);

public sealed record BalanceSheetSectionReadModel(
    string Name,
    decimal Total,
    IReadOnlyList<BalanceSheetAccountReadModel> Accounts);

public sealed record BalanceSheetAccountReadModel(
    long AccountId,
    string AccountCode,
    string AccountName,
    string AccountType,
    string NormalBalanceType,
    string? PresentationRole,
    decimal NormalBalance,
    decimal StatementBalance,
    decimal RollupNormalBalance,
    decimal RollupStatementBalance,
    IReadOnlyList<BalanceSheetAccountReadModel> Children);

public sealed record ComparativeBalanceSheetReadModel(
    BalanceSheetReportReadModel Current,
    BalanceSheetReportReadModel Comparison);
