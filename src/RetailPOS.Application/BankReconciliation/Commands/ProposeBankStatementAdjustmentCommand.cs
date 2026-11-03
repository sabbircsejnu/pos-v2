using MediatR;
using RetailPOS.Application.BankReconciliation.Abstractions;
using RetailPOS.Application.BankReconciliation.ReadModels;

namespace RetailPOS.Application.BankReconciliation.Commands;

public sealed record ProposeBankStatementAdjustmentCommand(
    long ActorUserId,
    ProposeBankStatementAdjustmentInput Input)
    : IRequest<StatementAdjustmentReadModel>;

public sealed class ProposeBankStatementAdjustmentCommandHandler(
    IBankStatementAdjustmentCommandStore commandStore,
    IBankReconciliationTenantScope tenantScope)
    : IRequestHandler<
        ProposeBankStatementAdjustmentCommand,
        StatementAdjustmentReadModel>
{
    public async Task<StatementAdjustmentReadModel> Handle(
        ProposeBankStatementAdjustmentCommand request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await commandStore.ProposeAsync(
            businessId,
            request.ActorUserId,
            request.Input,
            cancellationToken);
    }
}
