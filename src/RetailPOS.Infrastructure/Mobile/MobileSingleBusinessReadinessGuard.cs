using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RetailPOS.Application.Mobile;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Mobile;

public sealed class MobileSingleBusinessReadinessGuard(
    RetailPOSDbContext dbContext,
    ILogger<MobileSingleBusinessReadinessGuard> logger)
    : IMobileSingleBusinessReadinessGuard
{
    public async Task<bool> IsReadyAsync(long businessId, CancellationToken cancellationToken)
    {
        var businesses = await dbContext.Businesses
            .AsNoTracking()
            .OrderBy(business => business.Id)
            .Take(2)
            .Select(business => new { business.Id, business.IsActive })
            .ToListAsync(cancellationToken);

        if (businessId <= 0
            || businesses.Count != 1
            || !businesses[0].IsActive
            || businesses[0].Id != businessId)
        {
            logger.LogWarning(
                "Mobile sales dashboard denied because the deployment does not have exactly one active Business record matching the authenticated context.");
            return false;
        }

        var hasOutletsOutsideBusiness = await dbContext.Outlets
            .AsNoTracking()
            .AnyAsync(outlet => outlet.BusinessId != businessId, cancellationToken);

        if (hasOutletsOutsideBusiness)
        {
            logger.LogWarning(
                "Mobile sales dashboard denied because one or more Outlets are not owned by the sole operational Business.");
            return false;
        }

        return true;
    }
}
