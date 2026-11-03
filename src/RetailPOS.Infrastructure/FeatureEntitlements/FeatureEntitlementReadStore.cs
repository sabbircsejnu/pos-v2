using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.FeatureEntitlements.Abstractions;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.FeatureEntitlements;

public sealed class FeatureEntitlementReadStore(RetailPOSDbContext dbContext)
    : IFeatureEntitlementReadStore
{
    public async Task<bool> IsEnabledAsync(
        long businessId,
        string normalizedFeatureKey,
        CancellationToken cancellationToken)
    {
        var isEnabled = await dbContext.BusinessFeatureSettings
            .AsNoTracking()
            .Where(setting =>
                setting.BusinessId == businessId &&
                setting.FeatureKey == normalizedFeatureKey)
            .Select(setting => (bool?)setting.IsEnabled)
            .FirstOrDefaultAsync(cancellationToken);

        return isEnabled ?? true;
    }
}
