using RetailPOS.Application.FinancialYearCloseRuns.Abstractions;

namespace RetailPOS.API.Services;

public sealed class AccountingFinancialYearCloseTenantScopeAdapter(
    ITenantAccessService tenantAccess) : IAccountingFinancialYearCloseTenantScope
{
    public Task<long> GetBusinessIdAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(tenantAccess.RequireBusinessId());
    }
}
