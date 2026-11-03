using RetailPOS.Application.BankReconciliation.ReadModels;

namespace RetailPOS.Application.BankReconciliation.Abstractions;

public interface IBankStatementAdjustmentApprovalStore
{
    Task<StatementAdjustmentReadModel> ApproveAsync(
        long businessId,
        long actorUserId,
        long adjustmentId,
        DateTime postingDate,
        CancellationToken cancellationToken);
}

public interface IBankStatementAdjustmentJournalPoster
{
    Task<long> ResolvePostingAccountIdAsync(
        string adjustmentType,
        CancellationToken cancellationToken);

    Task<long> CreateAndPostAsync(
        BankStatementAdjustmentJournalRequest request,
        long actorUserId,
        CancellationToken cancellationToken);

    Task<long> ReverseAsync(
        long businessId,
        long journalEntryId,
        string reason,
        DateTime postingDate,
        long actorUserId,
        CancellationToken cancellationToken);
}

public sealed record BankStatementAdjustmentJournalRequest(
    long BusinessId,
    long AdjustmentId,
    string AdjustmentType,
    decimal Amount,
    DateTime PostingDate,
    string? Reason,
    string IdempotencyKey,
    long PostingAccountId,
    long BankChartAccountId);
