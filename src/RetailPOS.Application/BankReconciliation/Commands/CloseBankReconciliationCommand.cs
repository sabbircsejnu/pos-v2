using MediatR;
using RetailPOS.Application.BankReconciliation.Abstractions;
using RetailPOS.Application.BankReconciliation.ReadModels;

namespace RetailPOS.Application.BankReconciliation.Commands;

public sealed record CloseBankReconciliationCommand(
    long ActorUserId,
    long SessionId,
    string Reason) : IRequest<BankReconciliationSessionReadModel>;

public sealed class CloseBankReconciliationCommandHandler(
    IBankReconciliationCloseCommandStore commandStore,
    IBankReconciliationTenantScope tenantScope)
    : IRequestHandler<
        CloseBankReconciliationCommand,
        BankReconciliationSessionReadModel>
{
    public async Task<BankReconciliationSessionReadModel> Handle(
        CloseBankReconciliationCommand request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await commandStore.CloseAsync(
            businessId,
            request.ActorUserId,
            request.SessionId,
            request.Reason,
            cancellationToken);
    }
}
