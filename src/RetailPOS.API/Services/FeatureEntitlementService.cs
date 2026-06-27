using Microsoft.EntityFrameworkCore;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.API.Services;

public class FeatureEntitlementService : IFeatureEntitlementService
{
    private readonly RetailPOSDbContext _db;
    private readonly ITenantAccessService _tenantAccess;

    public FeatureEntitlementService(RetailPOSDbContext db, ITenantAccessService tenantAccess)
    {
        _db = db;
        _tenantAccess = tenantAccess;
    }

    public async Task<bool> IsFeatureEnabledAsync(string featureKey, long? businessId = null)
    {
        if (string.IsNullOrWhiteSpace(featureKey))
            return false;

        var resolvedBusinessId = ResolveBusinessId(businessId);
        if (!resolvedBusinessId.HasValue)
            return true;

        var normalizedKey = featureKey.Trim().ToLowerInvariant();

        var setting = await _db.BusinessFeatureSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.BusinessId == resolvedBusinessId.Value && f.FeatureKey == normalizedKey);

        // Missing row means feature is enabled by default.
        return setting?.IsEnabled ?? true;
    }

    public async Task EnsureFeatureEnabledAsync(string featureKey, long? businessId = null)
    {
        var enabled = await IsFeatureEnabledAsync(featureKey, businessId);
        if (!enabled)
            throw new UnauthorizedAccessException($"Feature '{featureKey}' is disabled for this business subscription.");
    }

    private long? ResolveBusinessId(long? businessId)
    {
        if (businessId.HasValue)
            return businessId;

        if (_tenantAccess.IsSuperAdmin)
            return null;

        return _tenantAccess.RequireBusinessId();
    }
}
