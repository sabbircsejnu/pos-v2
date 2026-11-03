using MediatR;
using RetailPOS.Application.BankReconciliation.Abstractions;
using RetailPOS.Application.BankReconciliation.ReadModels;

namespace RetailPOS.Application.BankReconciliation.Commands;

public sealed record ApproveBankStatementAdjustmentCommand(
    long ActorUserId,
    long AdjustmentId,
    DateTime PostingDate)
    : IRequest<StatementAdjustmentReadModel>;

public sealed class ApproveBankStatementAdjustmentCommandHandler(
    IBankStatementAdjustmentApprovalStore approvalStore,
    IBankReconciliationTenantScope tenantScope)
    : IRequestHandler<
        ApproveBankStatementAdjustmentCommand,
        StatementAdjustmentReadModel>
{
    public async Task<StatementAdjustmentReadModel> Handle(
        ApproveBankStatementAdjustmentCommand request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await approvalStore.ApproveAsync(
            businessId,
            request.ActorUserId,
            request.AdjustmentId,
            request.PostingDate,
            cancellationToken);
    }
}
