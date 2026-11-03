using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.HR.Capabilities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.HR.Capabilities;

public sealed class HrPayrollCapabilityReadStore(RetailPOSDbContext context)
    : IHrPayrollCapabilityReadStore
{
    public async Task<IReadOnlyDictionary<string, bool>> GetExplicitSettingsAsync(
        long businessId, CancellationToken cancellationToken)
    {
        if (businessId <= 0)
            throw new UnauthorizedAccessException("A positive Business scope is required.");

        return await context.BusinessFeatureSettings.AsNoTracking()
            .Where(setting => setting.BusinessId == businessId
                && HrPayrollCapabilityKeys.All.Contains(setting.FeatureKey))
            .ToDictionaryAsync(setting => setting.FeatureKey, setting => setting.IsEnabled,
                cancellationToken);
    }
}
