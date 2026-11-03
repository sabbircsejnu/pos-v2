using RetailPOS.Application.BankReconciliation.ReadModels;

namespace RetailPOS.Application.BankReconciliation.Abstractions;

public interface IBankReconciliationCloseCommandStore
{
    Task<BankReconciliationSessionReadModel> CloseAsync(
        long businessId,
        long actorUserId,
        long sessionId,
        string reason,
        CancellationToken cancellationToken);

    Task<BankReconciliationSessionReadModel> ReopenAsync(
        long businessId,
        long actorUserId,
        long sessionId,
        string reason,
        CancellationToken cancellationToken);

    Task<BankReconciliationSessionReadModel> PrepareForCloseAsync(
        long businessId,
        long actorUserId,
        long sessionId,
        CancellationToken cancellationToken);
}
