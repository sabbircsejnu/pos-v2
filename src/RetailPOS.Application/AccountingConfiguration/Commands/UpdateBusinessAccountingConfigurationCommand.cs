using MediatR;
using RetailPOS.Application.AccountingConfiguration.Abstractions;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Core.Entities;
using AccountingConfigurationEntity = RetailPOS.Core.Entities.AccountingConfiguration;

namespace RetailPOS.Application.AccountingConfiguration.Commands;

public sealed record UpdateBusinessAccountingConfigurationCommand(
    AccountingConfigurationEntity Configuration,
    long? ActorUserId) : ICommand<AccountingConfigurationEntity>;

public sealed class UpdateBusinessAccountingConfigurationCommandHandler(
    IAccountingConfigurationTenantScope tenantScope,
    IAccountingConfigurationWriteStore writeStore)
    : IRequestHandler<UpdateBusinessAccountingConfigurationCommand, AccountingConfigurationEntity>
{
    public async Task<AccountingConfigurationEntity> Handle(
        UpdateBusinessAccountingConfigurationCommand request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await writeStore.UpdateBusinessConfigurationAsync(
            businessId,
            request.Configuration,
            request.ActorUserId,
            cancellationToken);
    }
}
