using RetailPOS.Application.BankReconciliation.ReadModels;

namespace RetailPOS.Application.BankReconciliation.Abstractions;

public interface IBankReconciliationMatchCommandStore
{
    Task<ReconciliationMatchReadModel> CreateAsync(
        long businessId,
        long actorUserId,
        CreateBankReconciliationMatchInput input,
        CancellationToken cancellationToken);

    Task<ReconciliationMatchReadModel> UnmatchAsync(
        long businessId,
        long actorUserId,
        UnmatchBankReconciliationInput input,
        CancellationToken cancellationToken);
}

public sealed record CreateBankReconciliationMatchInput(
    long SessionId,
    IReadOnlyList<long> StatementLineIds,
    IReadOnlyList<long> JournalEntryLineIds);

public sealed record UnmatchBankReconciliationInput(long MatchId, string Reason);
