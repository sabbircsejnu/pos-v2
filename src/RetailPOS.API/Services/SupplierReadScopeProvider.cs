using RetailPOS.Application.Suppliers.Abstractions;

namespace RetailPOS.API.Services;

public sealed class SupplierReadScopeProvider(ITenantAccessService tenantAccess)
    : ISupplierReadScopeProvider
{
    public Task<long> GetBusinessIdAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(tenantAccess.RequireBusinessId());
    }
}
