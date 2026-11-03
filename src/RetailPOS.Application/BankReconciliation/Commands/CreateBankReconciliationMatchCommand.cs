using MediatR;
using RetailPOS.Application.BankReconciliation.Abstractions;
using RetailPOS.Application.BankReconciliation.ReadModels;

namespace RetailPOS.Application.BankReconciliation.Commands;

public sealed record CreateBankReconciliationMatchCommand(
    long ActorUserId,
    long SessionId,
    IReadOnlyList<long> StatementLineIds,
    IReadOnlyList<long> JournalEntryLineIds) : IRequest<ReconciliationMatchReadModel>;

public sealed class CreateBankReconciliationMatchCommandHandler(
    IBankReconciliationMatchCommandStore commandStore,
    IBankReconciliationTenantScope tenantScope)
    : IRequestHandler<CreateBankReconciliationMatchCommand, ReconciliationMatchReadModel>
{
    public async Task<ReconciliationMatchReadModel> Handle(
        CreateBankReconciliationMatchCommand request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await commandStore.CreateAsync(
            businessId,
            request.ActorUserId,
            new CreateBankReconciliationMatchInput(
                request.SessionId,
                request.StatementLineIds,
                request.JournalEntryLineIds),
            cancellationToken);
    }
}
