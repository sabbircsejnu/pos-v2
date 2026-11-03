using RetailPOS.Application.Outlets.Abstractions;

namespace RetailPOS.API.Services;

public sealed class OutletScopeProvider(ITenantAccessService tenantAccess)
    : IOutletScopeProvider
{
    public Task<long?> GetBusinessIdAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        long? businessId = tenantAccess.IsSuperAdmin
            ? null
            : tenantAccess.RequireBusinessId();
        return Task.FromResult(businessId);
    }
}
