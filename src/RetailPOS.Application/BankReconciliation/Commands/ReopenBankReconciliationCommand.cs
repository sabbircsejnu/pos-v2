using MediatR;
using RetailPOS.Application.BankReconciliation.Abstractions;
using RetailPOS.Application.BankReconciliation.ReadModels;

namespace RetailPOS.Application.BankReconciliation.Commands;

public sealed record ReopenBankReconciliationCommand(
    long ActorUserId,
    long SessionId,
    string Reason) : IRequest<BankReconciliationSessionReadModel>;

public sealed class ReopenBankReconciliationCommandHandler(
    IBankReconciliationCloseCommandStore commandStore,
    IBankReconciliationTenantScope tenantScope)
    : IRequestHandler<
        ReopenBankReconciliationCommand,
        BankReconciliationSessionReadModel>
{
    public async Task<BankReconciliationSessionReadModel> Handle(
        ReopenBankReconciliationCommand request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await commandStore.ReopenAsync(
            businessId,
            request.ActorUserId,
            request.SessionId,
            request.Reason,
            cancellationToken);
    }
}
