using MediatR;
using RetailPOS.Application.AccountingConfiguration.Abstractions;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Core.Entities;

namespace RetailPOS.Application.AccountingConfiguration.Commands;

public sealed record UpsertAccountingOutletConfigurationCommand(
    AccountingOutletConfiguration Configuration,
    long? ActorUserId) : ICommand<AccountingOutletConfiguration>;

public sealed class UpsertAccountingOutletConfigurationCommandHandler(
    IAccountingConfigurationTenantScope tenantScope,
    IAccountingOutletConfigurationWriteStore writeStore)
    : IRequestHandler<UpsertAccountingOutletConfigurationCommand, AccountingOutletConfiguration>
{
    public async Task<AccountingOutletConfiguration> Handle(
        UpsertAccountingOutletConfigurationCommand request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await writeStore.UpsertAsync(
            businessId,
            request.Configuration,
            request.ActorUserId,
            cancellationToken);
    }
}
