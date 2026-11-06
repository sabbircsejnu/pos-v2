using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Outlets.SalesTargets;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Outlets;

public sealed class OutletSalesTargetReadStore(RetailPOSDbContext dbContext) : IOutletSalesTargetReadStore
{
    public async Task<IReadOnlyDictionary<long, OutletSalesTargetSchedule>> GetSchedulesAsync(
        long businessId,
        IReadOnlyCollection<long> outletIds,
        CancellationToken cancellationToken)
    {
        var ids = outletIds.ToList();
        if (ids.Count == 0)
            return new Dictionary<long, OutletSalesTargetSchedule>();

        var versions = await dbContext.Set<OutletSalesTargetVersion>()
            .AsNoTracking()
            .AsSplitQuery()
            .Include(version => version.Days)
            .Where(version => version.BusinessId == businessId && ids.Contains(version.OutletId))
            .ToListAsync(cancellationToken);
        var overrides = await dbContext.Set<OutletSalesTargetOverride>()
            .AsNoTracking()
            .Where(item => item.BusinessId == businessId && ids.Contains(item.OutletId) && item.ClearedAt == null)
            .Select(item => new { item.OutletId, item.PeriodType, item.PeriodFrom, item.PeriodTo, item.OverrideAmount })
            .ToListAsync(cancellationToken);

        return ids.Distinct().ToDictionary(
            id => id,
            id => new OutletSalesTargetSchedule(
                id,
                versions
                    .Where(version => version.OutletId == id)
                    .Select(version => new SalesTargetVersionSnapshot(
                        version.VersionNo, version.EffectiveFrom, version.IsEnabled, ToDays(version)))
                    .ToList(),
                overrides
                    .Where(item => item.OutletId == id)
                    .Select(item => new SalesTargetOverrideSnapshot(
                        item.PeriodType, item.PeriodFrom, item.PeriodTo, item.OverrideAmount))
                    .ToList()));
    }

    public async Task<OutletSalesTargetStoredConfiguration?> GetConfigurationAsync(
        long? businessId,
        long outletId,
        CancellationToken cancellationToken)
    {
        var outlet = await dbContext.Outlets
            .AsNoTracking()
            .Where(candidate => candidate.Id == outletId && (businessId == null || candidate.BusinessId == businessId))
            .Select(candidate => new { candidate.Id, candidate.BusinessId })
            .FirstOrDefaultAsync(cancellationToken);
        if (outlet is null)
            return null;

        var versions = await dbContext.Set<OutletSalesTargetVersion>()
            .AsNoTracking()
            .AsSplitQuery()
            .Include(version => version.Days)
            .Where(version => version.OutletId == outletId)
            .OrderByDescending(version => version.VersionNo)
            .ToListAsync(cancellationToken);
        var overrides = await dbContext.Set<OutletSalesTargetOverride>()
            .AsNoTracking()
            .Where(item => item.OutletId == outletId && item.ClearedAt == null)
            .Select(item => new OutletSalesTargetStoredOverride(
                item.PeriodType,
                item.PeriodFrom,
                item.PeriodTo,
                item.OverrideAmount,
                item.Reason,
                item.UpdatedByUserId,
                item.UpdatedAt))
            .ToListAsync(cancellationToken);

        return new OutletSalesTargetStoredConfiguration(
            outlet.Id,
            outlet.BusinessId,
            versions
                .Select(version => new OutletSalesTargetVersionReadModel(
                    version.Id,
                    version.VersionNo,
                    version.EffectiveFrom,
                    version.IsEnabled,
                    ToDays(version),
                    version.CreatedByUserId,
                    version.CreatedAt))
                .ToList(),
            overrides);
    }

    private static List<SalesTargetDaySnapshot> ToDays(OutletSalesTargetVersion version) =>
        version.Days
            .OrderBy(day => day.DayOfWeek)
            .Select(day => new SalesTargetDaySnapshot(day.DayOfWeek, day.IsOffDay, day.TargetAmount))
            .ToList();
}
