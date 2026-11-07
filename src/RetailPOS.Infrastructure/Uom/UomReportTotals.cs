using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities.Uom;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Uom;

/// <summary>
/// A report total adds pcs to pcs only: 10 pcs + 500 ml is not a number (BI-002). Rows of other units still appear with
/// their own quantity, and money still sums. A business that only uses pcs sees exactly the totals it always did.
/// </summary>
internal static class UomReportTotals
{
    public static async Task<HashSet<long>> NonPcsVariantIdsAsync(RetailPOSDbContext dbContext, CancellationToken cancellationToken)
    {
        var ids = await dbContext.VariantUomConfigs.AsNoTracking()
            .Where(config => config.EffectiveToUtc == null && config.BaseUnit.Code != UomUnit.PcsCode)
            .Select(config => config.VariantId)
            .ToListAsync(cancellationToken);
        return ids.ToHashSet();
    }

    public static async Task<Dictionary<long, string>> BaseUnitCodesAsync(RetailPOSDbContext dbContext, long businessId, CancellationToken cancellationToken) =>
        await dbContext.VariantUomConfigs.AsNoTracking()
            .Where(config => config.BusinessId == businessId && config.EffectiveToUtc == null)
            .ToDictionaryAsync(config => config.VariantId, config => config.BaseUnit.Code, cancellationToken);
}
