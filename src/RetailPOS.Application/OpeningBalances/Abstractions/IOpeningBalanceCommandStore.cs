namespace RetailPOS.Application.OpeningBalances.Abstractions;

public interface IOpeningBalanceCommandStore
{
    Task<long> PostAsync(
        long businessId,
        long actorUserId,
        OpeningBalanceInput input,
        CancellationToken cancellationToken);

    Task<long> PostAdjustmentAsync(
        long businessId,
        long actorUserId,
        long openingBalanceJournalEntryId,
        OpeningBalanceAdjustmentInput input,
        CancellationToken cancellationToken);
}

public interface IOpeningBalanceQueryStore
{
    Task<OpeningBalanceStatus> GetStatusAsync(
        long businessId,
        CancellationToken cancellationToken);
}

public interface IOpeningBalanceMappingValidator
{
    Task<IReadOnlyList<string>> ValidateBalanceSheetMappingsAsync(
        long businessId,
        CancellationToken cancellationToken);
}

public interface IOpeningBalanceJournalPoster
{
    Task<long> CreateOrGetAndPostAsync(
        long businessId,
        long actorUserId,
        OpeningBalanceJournalInput journal,
        CancellationToken cancellationToken);
}

public sealed record OpeningBalanceInput(
    DateTime AccountingStartDate,
    string? ReferenceNumber,
    string? Description,
    IReadOnlyCollection<OpeningBalanceLineInput>? Lines);

public sealed record OpeningBalanceAdjustmentInput(
    string? Reason,
    DateTime AdjustmentBusinessDate,
    DateTime PostingDate,
    string? ReferenceNumber,
    string? Description,
    IReadOnlyCollection<OpeningBalanceLineInput>? Lines);

public sealed record OpeningBalanceLineInput(
    long AccountId,
    decimal DebitAmount,
    decimal CreditAmount,
    long? OutletId,
    long? WarehouseId,
    string? Description);

public sealed record OpeningBalanceJournalInput(
    string JournalType,
    DateTime TransactionDate,
    DateTime PostingDate,
    DateTime BusinessDate,
    string ReferenceType,
    string ReferenceId,
    string? ReferenceNumber,
    string Description,
    string? IdempotencyDiscriminator,
    IReadOnlyList<OpeningBalanceJournalLineInput> Lines);

public sealed record OpeningBalanceJournalLineInput(
    long AccountId,
    decimal DebitAmount,
    decimal CreditAmount,
    long? OutletId,
    long? WarehouseId,
    string? Description);

public sealed record OpeningBalanceStatus(
    bool HasPostedOpeningBalance,
    long? OpeningBalanceJournalEntryId,
    string? OpeningBalanceJournalNumber,
    DateTime? OpeningBalancePostedAt,
    bool IsOpeningBalanceLocked,
    long? LockingJournalEntryId,
    string? LockingJournalNumber,
    string? LockingJournalType,
    DateTime? LockedAt,
    bool CanPostOpeningBalance);
