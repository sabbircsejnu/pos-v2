namespace RetailPOS.Application.ApReports.ReadModels;

public sealed record ApPayablesReportFilter(
    DateTime? AsOfDate,
    DateTime? DateFrom,
    DateTime? DateTo,
    long? SupplierId,
    long? OutletId,
    long? WarehouseId,
    string? Status,
    bool? Disputed,
    string? AgingBucket,
    string? SourceReference);

public sealed record ApPayableReadModel(
    long PayableDocumentId,
    long SupplierId,
    string SupplierName,
    string SourceDocumentType,
    long SourceDocumentId,
    string SourceReference,
    DateTime RecognitionDate,
    string? SupplierInvoiceNumber,
    DateTime? SupplierInvoiceDate,
    DateTime DueDate,
    decimal OriginalPayableAmount,
    decimal EffectivePaymentAllocations,
    decimal EffectiveAdvanceUtilisations,
    decimal ReversedPaymentAllocations,
    decimal ReversedAdvanceUtilisations,
    decimal OutstandingAmount,
    string Status,
    bool IsDisputed,
    string? DisputeReason,
    long? WarehouseId,
    long? SourceOutletId,
    long SourceJournalEntryId,
    string? SourceJournalNumber);

public sealed record ApSupplierTotalReadModel(
    long SupplierId,
    string SupplierName,
    decimal TotalOutstanding,
    decimal DisputedAmount);

public sealed record ApExceptionSummaryReadModel(
    int LegacyUnreconciledCount,
    int SupplierOwnershipExceptionCount,
    IReadOnlyList<string> Details);

public sealed record ApSubledgerReadModel(
    DateTime AsOfDate,
    decimal TotalOutstanding,
    IReadOnlyList<ApPayableReadModel> Rows,
    IReadOnlyList<ApSupplierTotalReadModel> SupplierTotals,
    ApExceptionSummaryReadModel Exceptions);

public sealed record ApAgingReadModel(
    DateTime AsOfDate,
    decimal TotalOutstanding,
    decimal CurrentNotDue,
    decimal Overdue1To30,
    decimal Overdue31To60,
    decimal Overdue61To90,
    decimal Overdue90Plus,
    decimal DisputedAmount,
    IReadOnlyList<ApSupplierTotalReadModel> SupplierTotals,
    IReadOnlyList<ApPayableReadModel> Rows,
    ApExceptionSummaryReadModel Exceptions);

public sealed record SupplierStatementReadModel(
    long SupplierId,
    string SupplierName,
    DateTime DateFrom,
    DateTime DateTo,
    decimal OpeningBalance,
    decimal NewPayables,
    decimal SupplierPayments,
    decimal SupplierAdvanceApplications,
    decimal Reversals,
    decimal ClosingBalance,
    IReadOnlyList<SupplierStatementRowReadModel> Rows);

public sealed record SupplierStatementRowReadModel(
    DateTime Date,
    string EventType,
    long PayableDocumentId,
    string SourceReference,
    string? JournalReference,
    decimal Debit,
    decimal Credit,
    decimal RunningBalance);

public sealed record ApReconciliationReadModel(
    DateTime AsOfDate,
    long AccountId,
    string AccountSlot,
    decimal SubledgerTotal,
    decimal GeneralLedgerBalance,
    decimal ReconciliationDifference,
    IReadOnlyList<string> UnreconciledDetails);
