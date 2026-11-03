using MediatR;
using RetailPOS.Application.AccountingConfiguration.Abstractions;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Core.Entities;

namespace RetailPOS.Application.AccountingConfiguration.Queries;

public sealed record GetAccountingOutletConfigurationsQuery
    : IQuery<IReadOnlyList<AccountingOutletConfiguration>>;

public sealed class GetAccountingOutletConfigurationsQueryHandler(
    IAccountingConfigurationTenantScope tenantScope,
    IAccountingOutletConfigurationReadStore store)
    : IRequestHandler<GetAccountingOutletConfigurationsQuery, IReadOnlyList<AccountingOutletConfiguration>>
{
    public async Task<IReadOnlyList<AccountingOutletConfiguration>> Handle(
        GetAccountingOutletConfigurationsQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await store.GetForBusinessAsync(businessId, cancellationToken);
    }
}

public sealed record GetAccountingOutletConfigurationQuery(long OutletId)
    : IQuery<AccountingOutletConfiguration?>;

public sealed class GetAccountingOutletConfigurationQueryHandler(
    IAccountingConfigurationTenantScope tenantScope,
    IAccountingOutletConfigurationReadStore store)
    : IRequestHandler<GetAccountingOutletConfigurationQuery, AccountingOutletConfiguration?>
{
    public async Task<AccountingOutletConfiguration?> Handle(
        GetAccountingOutletConfigurationQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await store.GetForOutletAsync(businessId, request.OutletId, cancellationToken);
    }
}
