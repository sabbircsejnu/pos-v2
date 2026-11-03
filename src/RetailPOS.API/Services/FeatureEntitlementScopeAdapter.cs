using RetailPOS.Application.FeatureEntitlements.Abstractions;

namespace RetailPOS.API.Services;

public sealed class FeatureEntitlementScopeAdapter(ITenantAccessService tenantAccess)
    : IFeatureEntitlementScope
{
    public Task<long?> GetBusinessIdAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // A Super Admin is evaluated against the selected Business; with none selected the
        // platform-wide (null) scope applies so platform administrators are not blocked.
        return Task.FromResult<long?>(
            tenantAccess.IsSuperAdmin ? tenantAccess.EffectiveBusinessId : tenantAccess.RequireBusinessId());
    }
}
