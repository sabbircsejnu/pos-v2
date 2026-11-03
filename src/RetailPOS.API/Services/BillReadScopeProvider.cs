using RetailPOS.Application.Bills.Abstractions;

namespace RetailPOS.API.Services;

public sealed class BillReadScopeProvider(ITenantAccessService tenantAccess)
    : IBillReadScopeProvider
{
    public Task<long> GetBusinessIdAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(tenantAccess.RequireBusinessId());
    }
}
