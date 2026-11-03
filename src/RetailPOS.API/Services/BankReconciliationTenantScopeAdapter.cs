using RetailPOS.Application.BankReconciliation.Abstractions;

namespace RetailPOS.API.Services;

public sealed class BankReconciliationTenantScopeAdapter(ITenantAccessService tenantAccess)
    : IBankReconciliationTenantScope
{
    public Task<long> GetBusinessIdAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(tenantAccess.RequireBusinessId());
    }
}
