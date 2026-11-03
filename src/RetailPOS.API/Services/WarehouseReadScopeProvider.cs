using RetailPOS.Application.Warehouses.Abstractions;

namespace RetailPOS.API.Services;

public sealed class WarehouseReadScopeProvider(ITenantAccessService tenantAccess)
    : IWarehouseReadScopeProvider
{
    public Task<long?> GetBusinessIdAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        long? businessId = tenantAccess.RequireBusinessId();
        return Task.FromResult(businessId);
    }
}
