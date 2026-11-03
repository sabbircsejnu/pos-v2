using RetailPOS.Application.BankReconciliation.ReadModels;

namespace RetailPOS.Application.BankReconciliation.Abstractions;

public interface IBankStatementAdjustmentReversalStore
{
    Task<StatementAdjustmentReadModel> ReverseAsync(
        long businessId,
        long actorUserId,
        long adjustmentId,
        string reason,
        DateTime postingDate,
        CancellationToken cancellationToken);
}
