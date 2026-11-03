using MediatR;
using RetailPOS.Application.AccountingConfiguration.Abstractions;
using RetailPOS.Application.Abstractions.Messaging;

namespace RetailPOS.Application.AccountingConfiguration.Queries;

public sealed record GetBusinessConfigurationQuery : IQuery<RetailPOS.Core.Entities.AccountingConfiguration>;

public sealed class GetBusinessConfigurationQueryHandler(
    IAccountingConfigurationTenantScope tenantScope,
    IAccountingConfigurationInitializationStore store)
    : IRequestHandler<GetBusinessConfigurationQuery, RetailPOS.Core.Entities.AccountingConfiguration>
{
    public async Task<RetailPOS.Core.Entities.AccountingConfiguration> Handle(
        GetBusinessConfigurationQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await store.EnsureInitializedAsync(
            businessId,
            actorUserId: null,
            cancellationToken);
    }
}
