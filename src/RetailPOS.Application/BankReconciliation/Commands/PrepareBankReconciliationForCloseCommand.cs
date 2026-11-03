using MediatR;
using RetailPOS.Application.BankReconciliation.Abstractions;
using RetailPOS.Application.BankReconciliation.ReadModels;

namespace RetailPOS.Application.BankReconciliation.Commands;

public sealed record PrepareBankReconciliationForCloseCommand(
    long ActorUserId,
    long SessionId) : IRequest<BankReconciliationSessionReadModel>;

public sealed class PrepareBankReconciliationForCloseCommandHandler(
    IBankReconciliationCloseCommandStore commandStore,
    IBankReconciliationTenantScope tenantScope)
    : IRequestHandler<
        PrepareBankReconciliationForCloseCommand,
        BankReconciliationSessionReadModel>
{
    public async Task<BankReconciliationSessionReadModel> Handle(
        PrepareBankReconciliationForCloseCommand request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await commandStore.PrepareForCloseAsync(
            businessId,
            request.ActorUserId,
            request.SessionId,
            cancellationToken);
    }
}
