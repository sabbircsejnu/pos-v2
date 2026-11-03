using RetailPOS.Application.AccountingCloseRuns.Abstractions;

namespace RetailPOS.API.Services;

public sealed class AccountingPeriodCloseTenantScopeAdapter(
    ITenantAccessService tenantAccess) : IAccountingPeriodCloseTenantScope
{
    public Task<long> GetBusinessIdAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(tenantAccess.RequireBusinessId());
    }
}
