using RetailPOS.Application.BankReconciliation.ReadModels;

namespace RetailPOS.Application.BankReconciliation.Abstractions;

public sealed record ProposeBankStatementAdjustmentInput(
    long BankStatementLineId,
    string AdjustmentType,
    decimal Amount,
    string Reason,
    string IdempotencyKey);

public interface IBankStatementAdjustmentCommandStore
{
    Task<StatementAdjustmentReadModel> ProposeAsync(
        long businessId,
        long actorUserId,
        ProposeBankStatementAdjustmentInput input,
        CancellationToken cancellationToken);
}
