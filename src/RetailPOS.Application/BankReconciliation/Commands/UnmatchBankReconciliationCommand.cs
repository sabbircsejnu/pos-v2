using MediatR;
using RetailPOS.Application.BankReconciliation.Abstractions;
using RetailPOS.Application.BankReconciliation.ReadModels;

namespace RetailPOS.Application.BankReconciliation.Commands;

public sealed record UnmatchBankReconciliationCommand(
    long ActorUserId,
    long MatchId,
    string Reason) : IRequest<ReconciliationMatchReadModel>;

public sealed class UnmatchBankReconciliationCommandHandler(
    IBankReconciliationMatchCommandStore commandStore,
    IBankReconciliationTenantScope tenantScope)
    : IRequestHandler<UnmatchBankReconciliationCommand, ReconciliationMatchReadModel>
{
    public async Task<ReconciliationMatchReadModel> Handle(
        UnmatchBankReconciliationCommand request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await commandStore.UnmatchAsync(
            businessId,
            request.ActorUserId,
            new UnmatchBankReconciliationInput(request.MatchId, request.Reason),
            cancellationToken);
    }
}
