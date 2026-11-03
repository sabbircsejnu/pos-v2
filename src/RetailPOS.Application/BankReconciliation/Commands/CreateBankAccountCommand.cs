using MediatR;
using RetailPOS.Application.BankReconciliation.Abstractions;
using RetailPOS.Application.BankReconciliation.ReadModels;

namespace RetailPOS.Application.BankReconciliation.Commands;

public sealed record CreateBankAccountCommand(
    long? ActorUserId,
    CreateBankAccountInput Input)
    : IRequest<BankAccountReadModel>;

public sealed class CreateBankAccountCommandHandler(
    IBankAccountCommandStore bankAccountStore,
    IBankReconciliationTenantScope tenantScope)
    : IRequestHandler<CreateBankAccountCommand, BankAccountReadModel>
{
    public async Task<BankAccountReadModel> Handle(
        CreateBankAccountCommand request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await bankAccountStore.CreateAsync(
            businessId,
            request.ActorUserId,
            request.Input,
            cancellationToken);
    }
}
