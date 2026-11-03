using RetailPOS.Application.AccountingConfiguration.Abstractions;
using RetailPOS.Core.Audit;

namespace RetailPOS.API.Services;

public sealed class AccountingConfigurationTenantScopeAdapter(
    ITenantAccessService tenantAccess,
    IAuditContext? audit = null) : IAccountingConfigurationTenantScope
{
    public Task<long> GetBusinessIdAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(IntegrationJobBusinessScope.RequireBusinessId(tenantAccess, audit));
    }
}
