using RetailPOS.Application.FinancialPeriods.Abstractions;

namespace RetailPOS.API.Services;

public sealed class FinancialPeriodTenantScopeAdapter(ITenantAccessService tenantAccess)
    : IFinancialPeriodTenantScope
{
    public Task<long> GetBusinessIdAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(tenantAccess.RequireBusinessId());
    }
}
