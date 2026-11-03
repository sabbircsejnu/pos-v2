namespace RetailPOS.Application.AccountingReports.ReadModels;

public sealed record CashBookFilter(
    DateTime? DateFrom,
    DateTime? DateTo,
    long? LocationId,
    string? LocationType,
    string? JournalType,
    string? ReferenceType,
    string? Reference);

public sealed record CashBookAccountResolution(string Slot, long AccountId);

public sealed record CashBookReadModel(
    DateTime DateFrom,
    DateTime DateTo,
    IReadOnlyList<CashBookAccountReadModel> Accounts);

public sealed record CashBookAccountReadModel(
    string AccountSlot,
    long AccountId,
    string AccountCode,
    string AccountName,
    decimal OpeningBalance,
    decimal TotalInflows,
    decimal TotalOutflows,
    decimal ClosingBalance,
    decimal GeneralLedgerBalance,
    decimal ReconciliationDifference,
    IReadOnlyList<CashBookRowReadModel> Rows);

public sealed record CashBookRowReadModel(
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
    decimal Inflow,
    decimal Outflow,
    decimal RunningBalance,
    long? OutletId,
    long? WarehouseId,
    long? CustomerId,
    long? SupplierId,
    string? PaymentMethodId,
    string? CostCenterId,
    string CounterpartAccounts,
    IReadOnlyList<long> CounterpartAccountIds,
    long? ReversalOfJournalEntryId,
    long? ReversedByJournalEntryId);
