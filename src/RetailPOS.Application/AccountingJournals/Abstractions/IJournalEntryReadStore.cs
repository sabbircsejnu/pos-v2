namespace RetailPOS.Application.AccountingJournals.Abstractions;

public interface IJournalEntryReadStore
{
    Task<JournalListReadModel> SearchAsync(
        JournalSearchFilter filter,
        CancellationToken cancellationToken);

    Task<JournalEntryReadModel> GetByIdAsync(
        long businessId,
        long journalEntryId,
        CancellationToken cancellationToken);
}

public sealed record JournalSearchFilter(
    long BusinessId,
    DateTime? PostingDateStart,
    DateTime? PostingDateEnd,
    DateTime? BusinessDateStart,
    DateTime? BusinessDateEnd,
    string? Status,
    long? OutletId,
    string? JournalType,
    string? JournalNumber,
    long? AccountId,
    string? ReferenceType,
    string? ReferenceNumber,
    long? CreatedBy,
    long? PostedBy,
    string? SortBy,
    string? SortDirection,
    int PageNumber,
    int PageSize);

public sealed record JournalListReadModel(
    IReadOnlyList<JournalEntryReadModel> Entries,
    int TotalCount,
    int PageNumber,
    int PageSize);

public sealed record JournalEntryReadModel(
    long Id,
    long BusinessId,
    string? JournalNumber,
    string JournalType,
    DateTime? BusinessDate,
    DateTime TransactionDate,
    DateTime PostingDate,
    long? FinancialYearId,
    long? AccountingPeriodId,
    string ReferenceType,
    string ReferenceId,
    string? ReferenceNumber,
    string? Description,
    long? OutletId,
    long? WarehouseId,
    long? TerminalId,
    long? CashSessionId,
    string Status,
    bool IsSystemGenerated,
    long? ReversalOfJournalEntryId,
    long? ReversedByJournalEntryId,
    DateTime? SubmittedAt,
    long? SubmittedBy,
    DateTime? ApprovedAt,
    long? ApprovedBy,
    DateTime? RejectedAt,
    long? RejectedBy,
    string? RejectionReason,
    DateTime CreatedAt,
    long? CreatedBy,
    DateTime UpdatedAt,
    long? UpdatedBy,
    DateTime? PostedAt,
    long? PostedBy,
    DateTime? CancelledAt,
    long? CancelledBy,
    string? CancellationReason,
    DateTime? ReversedAt,
    long? ReversedBy,
    IReadOnlyList<JournalEntryLineReadModel> Lines,
    IReadOnlyList<JournalEntryAttachmentReadModel> Attachments);

public sealed record JournalEntryLineReadModel(
    long Id,
    long AccountId,
    string? Description,
    decimal DebitAmount,
    decimal CreditAmount,
    long? OutletId,
    long? WarehouseId,
    long? SupplierId,
    long? CustomerId,
    string? PaymentMethodId,
    string? CostCenterId);

public sealed record JournalEntryAttachmentReadModel(
    long Id,
    string FileName,
    string ContentType,
    long FileSizeBytes,
    DateTime CreatedAt,
    long? CreatedBy);
