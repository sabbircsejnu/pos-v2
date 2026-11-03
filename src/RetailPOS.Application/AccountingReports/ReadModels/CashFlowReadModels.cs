namespace RetailPOS.Application.AccountingReports.ReadModels;

public sealed record CashFlowFilter(
    DateTime? DateFrom,
    DateTime? DateTo,
    long? LocationId,
    string? LocationType,
    string? JournalType,
    string? ReferenceType,
    string? Reference,
    DateTime? ComparisonDateFrom,
    DateTime? ComparisonDateTo);

public sealed record CashFlowReportReadModel(
    DateTime DateFrom,
    DateTime DateTo,
    string Method,
    decimal OpeningLiquidity,
    decimal ClosingLiquidity,
    decimal OperatingNetMovement,
    decimal InvestingNetMovement,
    decimal FinancingNetMovement,
    decimal UnclassifiedNetMovement,
    decimal TotalExternalNetMovement,
    decimal GlDerivedMovement,
    decimal ReconciliationDifference,
    decimal InternalTransferNetMovement,
    decimal CutoverAndNonCashNetMovement,
    IReadOnlyList<CashFlowSectionReadModel> Sections,
    IReadOnlyList<CashFlowRowReadModel> InternalTransferRows,
    IReadOnlyList<CashFlowRowReadModel> CutoverAndNonCashRows);

public sealed record CashFlowSectionReadModel(
    string ActivityType,
    decimal TotalInflows,
    decimal TotalOutflows,
    decimal NetMovement,
    IReadOnlyList<CashFlowRowReadModel> Rows);

public sealed record CashFlowRowReadModel(
    long JournalEntryId,
    long JournalEntryLineId,
    DateTime PostingDate,
    DateTime? BusinessDate,
    string? JournalNumber,
    string JournalType,
    string ReferenceType,
    string ReferenceId,
    string? ReferenceNumber,
    string? Narration,
    string AccountSlot,
    long AccountId,
    string AccountCode,
    string AccountName,
    decimal Inflow,
    decimal Outflow,
    decimal NetMovement,
    string ClassificationReason,
    string CounterpartAccounts,
    IReadOnlyList<long> CounterpartAccountIds,
    long? OutletId,
    long? WarehouseId,
    long? CustomerId,
    long? SupplierId,
    string? PaymentMethodId,
    string? CostCenterId,
    long? ReversalOfJournalEntryId,
    long? ReversedByJournalEntryId);

public sealed record ComparativeCashFlowReadModel(
    CashFlowReportReadModel Current,
    CashFlowReportReadModel Comparison);
