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

/// <summary>
/// A variant may only be deleted while nothing refers to it. Its own unit configuration goes with it, because a variant
/// with no transactions has no snapshot that needs it. Being another item's packaging, or having repack jobs or physical
/// stock states, keeps it.
/// </summary>
internal static class UomVariantRemoval
{
    public static async Task RemoveConfigurationAsync(RetailPOSDbContext dbContext, IReadOnlyCollection<long> variantIds, CancellationToken cancellationToken)
    {
        var ids = variantIds.ToList();
        if (await dbContext.SellingOptionComponents.AnyAsync(row => ids.Contains(row.ComponentVariantId) && row.SellingOption.Config.EffectiveToUtc == null, cancellationToken))
            throw new InvalidOperationException("This item is used as packaging by another item. Remove it from that item's selling options first.");
        if (await dbContext.RepackJobs.AnyAsync(row => ids.Contains(row.SourceVariantId), cancellationToken)
            || await dbContext.RepackComponentLines.AnyAsync(row => ids.Contains(row.ComponentVariantId), cancellationToken)
            || await dbContext.SaleItemComponents.AnyAsync(row => ids.Contains(row.ComponentVariantId), cancellationToken)
            || await dbContext.PackStateBalances.AnyAsync(row => ids.Contains(row.VariantId) && (row.BaseQuantity != 0m || row.PackCount != 0), cancellationToken))
            throw new InvalidOperationException("This item has repacking or physical stock history, so it cannot be deleted.");

        var configs = await dbContext.VariantUomConfigs
            .Include(config => config.PurchaseOptions)
            .Include(config => config.SellingOptions).ThenInclude(option => option.Components)
            .Where(config => ids.Contains(config.VariantId))
            .ToListAsync(cancellationToken);
        dbContext.SellingOptionComponents.RemoveRange(configs.SelectMany(config => config.SellingOptions).SelectMany(option => option.Components));
        dbContext.VariantSellingOptions.RemoveRange(configs.SelectMany(config => config.SellingOptions));
        dbContext.VariantPurchaseUnitOptions.RemoveRange(configs.SelectMany(config => config.PurchaseOptions));
        dbContext.VariantUomConfigs.RemoveRange(configs);
        dbContext.PackStateBalances.RemoveRange(await dbContext.PackStateBalances.Where(row => ids.Contains(row.VariantId)).ToListAsync(cancellationToken));
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
