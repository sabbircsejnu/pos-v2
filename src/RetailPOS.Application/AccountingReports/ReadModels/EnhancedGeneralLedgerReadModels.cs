namespace RetailPOS.Application.AccountingReports.ReadModels;

public sealed record EnhancedGeneralLedgerFilter(
    long AccountId,
    DateTime? DateFrom,
    DateTime? DateTo,
    long? LocationId,
    string? LocationType,
    string? JournalType,
    string? ReferenceType,
    string? Reference);

public sealed record EnhancedGeneralLedgerReadModel(
    long AccountId,
    string AccountCode,
    string AccountName,
    string NormalBalanceType,
    decimal OpeningBalance,
    decimal TotalDebits,
    decimal TotalCredits,
    decimal ClosingBalance,
    IReadOnlyList<EnhancedGeneralLedgerRowReadModel> Rows);

public sealed record EnhancedGeneralLedgerRowReadModel(
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
    decimal DebitAmount,
    decimal CreditAmount,
    decimal RunningBalance,
    long? OutletId,
    long? WarehouseId,
    long? CustomerId,
    long? SupplierId,
    string? PaymentMethodId,
    string? CostCenterId,
    long? ReversalOfJournalEntryId,
    long? ReversedByJournalEntryId);
