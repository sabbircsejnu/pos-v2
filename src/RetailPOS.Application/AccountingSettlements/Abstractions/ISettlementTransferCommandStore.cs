using RetailPOS.Application.AccountingSettlements.ReadModels;

namespace RetailPOS.Application.AccountingSettlements.Abstractions;

public interface ISettlementTransferCommandStore
{
    Task<SettlementBatchReadModel> PostSettlementAsync(
        long businessId,
        long actorUserId,
        SettlementPostingInput input,
        CancellationToken cancellationToken);

    Task<SettlementBatchReadModel> ReverseSettlementAsync(
        long businessId,
        long actorUserId,
        long settlementId,
        SettlementReversalInput input,
        CancellationToken cancellationToken);

    Task<LiquidityTransferReadModel> PostTransferAsync(
        long businessId,
        long actorUserId,
        LiquidityTransferPostingInput input,
        CancellationToken cancellationToken);

    Task<LiquidityTransferReadModel> ReverseTransferAsync(
        long businessId,
        long actorUserId,
        long transferId,
        LiquidityTransferReversalInput input,
        CancellationToken cancellationToken);
}

public interface ISettlementPostingAccountResolver
{
    Task<long> ResolveAsync(
        long businessId,
        string slot,
        CancellationToken cancellationToken);
}

public interface ISettlementJournalPoster
{
    Task<long> CreateOrGetAndPostAsync(
        long businessId,
        long actorUserId,
        SettlementJournalInput journal,
        CancellationToken cancellationToken);
}

public interface ISettlementJournalReverser
{
    Task<long> ReverseAsync(
        long businessId,
        long actorUserId,
        long journalEntryId,
        SettlementJournalReversalInput input,
        CancellationToken cancellationToken);
}

public sealed record SettlementPostingInput(
    string SettlementType,
    long ClearingAccountId,
    long BankAccountId,
    DateTime SettlementDate,
    decimal GrossClearingAmount,
    decimal SettlementFeeAmount,
    string? ExternalReference,
    string IdempotencyKey);

public sealed record LiquidityTransferPostingInput(
    string TransferType,
    long? SourceBankAccountId,
    long? DestinationBankAccountId,
    decimal Amount,
    DateTime PostingDate,
    string? Reference,
    string IdempotencyKey);

public sealed record LiquidityTransferReversalInput(
    string Reason,
    DateTime PostingDate);

public sealed record SettlementJournalInput(
    string JournalType,
    DateTime TransactionDate,
    DateTime PostingDate,
    DateTime BusinessDate,
    string ReferenceType,
    string ReferenceId,
    string? ReferenceNumber,
    string Description,
    string IdempotencyDiscriminator,
    IReadOnlyList<SettlementJournalLineInput> Lines);

public sealed record SettlementJournalLineInput(
    long AccountId,
    string Description,
    decimal DebitAmount,
    decimal CreditAmount);

public sealed record SettlementReversalInput(
    string Reason,
    DateTime PostingDate);

public sealed record SettlementJournalReversalInput(
    string Reason,
    DateTime PostingDate,
    DateTime BusinessDate);
