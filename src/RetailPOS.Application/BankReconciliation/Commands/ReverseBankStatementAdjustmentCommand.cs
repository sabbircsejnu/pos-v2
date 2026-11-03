using MediatR;
using RetailPOS.Application.BankReconciliation.Abstractions;
using RetailPOS.Application.BankReconciliation.ReadModels;

namespace RetailPOS.Application.BankReconciliation.Commands;

public sealed record ReverseBankStatementAdjustmentCommand(
    long ActorUserId,
    long AdjustmentId,
    string Reason,
    DateTime PostingDate)
    : IRequest<StatementAdjustmentReadModel>;

public sealed class ReverseBankStatementAdjustmentCommandHandler(
    IBankStatementAdjustmentReversalStore reversalStore,
    IBankReconciliationTenantScope tenantScope)
    : IRequestHandler<
        ReverseBankStatementAdjustmentCommand,
        StatementAdjustmentReadModel>
{
    public async Task<StatementAdjustmentReadModel> Handle(
        ReverseBankStatementAdjustmentCommand request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await reversalStore.ReverseAsync(
            businessId,
            request.ActorUserId,
            request.AdjustmentId,
            request.Reason,
            request.PostingDate,
            cancellationToken);
    }
}
