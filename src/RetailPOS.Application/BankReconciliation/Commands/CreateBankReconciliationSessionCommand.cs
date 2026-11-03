using MediatR;
using RetailPOS.Application.BankReconciliation.Abstractions;
using RetailPOS.Application.BankReconciliation.ReadModels;

namespace RetailPOS.Application.BankReconciliation.Commands;

public sealed record CreateBankReconciliationSessionCommand(
    long ActorUserId,
    long BankAccountId,
    DateTime PeriodStart,
    DateTime PeriodEnd) : IRequest<BankReconciliationSessionReadModel>;

public sealed class CreateBankReconciliationSessionCommandHandler(
    IBankReconciliationSessionCommandStore commandStore,
    IBankReconciliationTenantScope tenantScope)
    : IRequestHandler<
        CreateBankReconciliationSessionCommand,
        BankReconciliationSessionReadModel>
{
    public async Task<BankReconciliationSessionReadModel> Handle(
        CreateBankReconciliationSessionCommand request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await commandStore.CreateAsync(
            businessId,
            request.ActorUserId,
            new CreateBankReconciliationSessionInput(
                request.BankAccountId,
                request.PeriodStart,
                request.PeriodEnd),
            cancellationToken);
    }
}
