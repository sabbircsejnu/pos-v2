namespace RetailPOS.Application.AccountingReports.ReadModels;

public sealed record GeneralLedgerReportFilter(
    DateTime? DateFrom,
    DateTime? DateTo,
    long? OutletId,
    long? FinancialYearId,
    long? AccountingPeriodId);

public sealed record GeneralLedgerReportReadModel(
    IReadOnlyList<GeneralLedgerRowReadModel> Rows);

public sealed record GeneralLedgerRowReadModel(
    string AccountCode,
    string AccountName,
    string JournalNumber,
    decimal DebitAmount,
    decimal CreditAmount,
    decimal RunningBalance);

public sealed record TrialBalanceReportReadModel(
    IReadOnlyList<TrialBalanceRowReadModel> Rows,
    decimal TotalDebits,
    decimal TotalCredits);

public sealed record TrialBalanceRowReadModel(
    string AccountCode,
    string AccountName,
    decimal DebitAmount,
    decimal CreditAmount);

public sealed record ProfitLossReportReadModel(
    decimal Revenue,
    decimal CostOfGoodsSold,
    decimal Expenses,
    decimal NetProfit);

public sealed record ComparativeProfitLossReportFilter(
    DateTime? DateFrom,
    DateTime? DateTo,
    DateTime? ComparisonDateFrom,
    DateTime? ComparisonDateTo,
    long? LocationId,
    string? LocationType);

public sealed record ComparativeProfitLossReadModel(
    ProfitLossReportReadModel Current,
    ProfitLossReportReadModel Comparison);
