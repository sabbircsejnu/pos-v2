namespace RetailPOS.Application.AccountingJournals.Abstractions;

public interface IManualJournalDraftStore
{
    Task<long> CreateNewSystemDraftAsync(
        long businessId,
        long actorUserId,
        ManualJournalDraftInput journal,
        string? idempotencyDiscriminator,
        CancellationToken cancellationToken);

    Task<long> CreateOrGetSystemDraftAsync(
        long businessId,
        long actorUserId,
        ManualJournalDraftInput journal,
        string? idempotencyDiscriminator,
        CancellationToken cancellationToken);

    Task<long> CreateAsync(
        long businessId,
        long actorUserId,
        ManualJournalDraftInput journal,
        CancellationToken cancellationToken);

    Task<long> UpdateAsync(
        long businessId,
        long actorUserId,
        long journalId,
        ManualJournalDraftUpdateInput journal,
        CancellationToken cancellationToken);

    Task<long> SubmitAsync(
        long businessId,
        long actorUserId,
        long journalId,
        CancellationToken cancellationToken);

    Task<long> ApproveAsync(
        long businessId,
        long actorUserId,
        long journalId,
        bool isBusinessOwnerOrSuperAdmin,
        CancellationToken cancellationToken);

    Task<long> RejectAsync(
        long businessId,
        long actorUserId,
        long journalId,
        string reason,
        CancellationToken cancellationToken);

    Task<long> CancelAsync(
        long businessId,
        long actorUserId,
        long journalId,
        string? reason,
        CancellationToken cancellationToken);

    Task<long> PostAsync(
        long businessId,
        long actorUserId,
        long journalId,
        CancellationToken cancellationToken);

    Task<long> PostApprovedYearEndClosingAsync(
        long businessId,
        long actorUserId,
        long journalId,
        long financialYearCloseRunId,
        CancellationToken cancellationToken);

    Task<long> ReverseAsync(
        long businessId,
        long actorUserId,
        long journalId,
        ManualJournalReversalInput input,
        CancellationToken cancellationToken);
}

public sealed record ManualJournalDraftInput(
    string? JournalType,
    DateTime TransactionDate,
    DateTime PostingDate,
    DateTime? BusinessDate,
    string? ReferenceType,
    string? ReferenceId,
    string? ReferenceNumber,
    string? Description,
    long? OutletId,
    long? WarehouseId,
    long? TerminalId,
    long? CashSessionId,
    IReadOnlyList<ManualJournalDraftLineInput> Lines,
    string? CurrencyCode = null,
    int? SourceRevision = null,
    string? SourceEvidenceHash = null);

public sealed record ManualJournalDraftLineInput(
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

public sealed record ManualJournalDraftUpdateInput(
    string? JournalType,
    DateTime TransactionDate,
    DateTime PostingDate,
    DateTime? BusinessDate,
    string? ReferenceType,
    string? ReferenceId,
    string? ReferenceNumber,
    string? Description,
    long? OutletId,
    long? WarehouseId,
    long? TerminalId,
    long? CashSessionId,
    IReadOnlyList<ManualJournalDraftLineInput> Lines);

public sealed record ManualJournalReversalInput(
    string Reason,
    DateTime? PostingDate,
    DateTime? BusinessDate);
