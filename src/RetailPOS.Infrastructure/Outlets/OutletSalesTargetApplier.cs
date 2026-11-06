using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Outlets;
using RetailPOS.Application.Outlets.SalesTargets;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Outlets;

/// <summary>What a save changed, for the audit record.</summary>
internal sealed record OutletSalesTargetChange(
    OutletSalesTargetVersion? NewVersion,
    SalesTargetVersionSnapshot? PreviousVersion,
    IReadOnlyList<OutletSalesTargetOverride> ChangedOverrides,
    IReadOnlyList<OutletSalesTargetOverride> ClearedOverrides)
{
    public bool HasChanges => NewVersion is not null || ChangedOverrides.Count > 0 || ClearedOverrides.Count > 0;
}

/// <summary>
/// Adds the target configuration to an outlet create/edit before its single save, so outlet and target commit together.
/// Saved versions are never edited: a change appends a new version, and saving unchanged values adds nothing.
/// </summary>
internal static class OutletSalesTargetApplier
{
    public static async Task<OutletSalesTargetChange> ApplyAsync(
        RetailPOSDbContext db,
        Outlet outlet,
        OutletSalesTargetInput input,
        DateOnly today,
        long? actorUserId,
        bool isNew,
        CancellationToken cancellationToken)
    {
        SalesTargetRules.Validate(input);
        if (outlet.BusinessId is not long businessId)
            throw new InvalidOperationException("Sales targets can only be set on an outlet that belongs to a business.");

        var stored = isNew
            ? []
            : await db.Set<OutletSalesTargetVersion>()
                .AsNoTracking()
                .Include(version => version.Days)
                .Where(version => version.OutletId == outlet.Id)
                .ToListAsync(cancellationToken);
        var versions = stored.Select(ToSnapshot).ToList();
        var latestStored = stored.MaxBy(version => version.VersionNo);
        var latest = latestStored is null ? null : ToSnapshot(latestStored);

        if (input.ExpectedVersionNo != latest?.VersionNo)
            throw new OutletSetupException(
                SalesTargetErrorCodes.Conflict,
                "Someone else changed this outlet's sales targets. Reload the page and try again.");

        var days = SalesTargetRules.Normalize(input.Days);
        var now = DateTime.UtcNow;
        OutletSalesTargetVersion? added = null;
        if (IsChange(latest, input.IsEnabled, days))
        {
            RequireEffectiveDate(input.EffectiveFrom, today, latest);
            added = new OutletSalesTargetVersion
            {
                BusinessId = businessId,
                VersionNo = (latest?.VersionNo ?? 0) + 1,
                EffectiveFrom = input.EffectiveFrom,
                IsEnabled = input.IsEnabled,
                CreatedByUserId = actorUserId,
                CreatedAt = now,
                Days = days
                    .Select(day => new OutletSalesTargetVersionDay
                    {
                        DayOfWeek = day.DayOfWeek,
                        IsOffDay = day.IsOffDay,
                        TargetAmount = day.TargetAmount
                    })
                    .ToList()
            };
            outlet.SalesTargetVersions.Add(added);
            versions.Add(new SalesTargetVersionSnapshot(added.VersionNo, added.EffectiveFrom, added.IsEnabled, days));
        }

        var (changed, cleared) = await ApplyOverridesAsync(
            db, outlet, input, new OutletSalesTargetSchedule(outlet.Id, versions, []), added, latestStored, businessId, actorUserId, now, cancellationToken);
        return new OutletSalesTargetChange(added, latest, changed, cleared);
    }

    private static bool IsChange(
        SalesTargetVersionSnapshot? latest,
        bool isEnabled,
        IReadOnlyList<SalesTargetDaySnapshot> days) =>
        latest is null
            ? isEnabled || days.Any(day => day.IsOffDay)
            : !SalesTargetRules.SameConfiguration(latest, isEnabled, days);

    /// <summary>Past reporting never changes, so a new version starts today at the earliest and keeps versions in order.</summary>
    private static void RequireEffectiveDate(DateOnly effectiveFrom, DateOnly today, SalesTargetVersionSnapshot? latest)
    {
        if (effectiveFrom < today)
            throw new OutletSetupException(
                SalesTargetErrorCodes.InvalidEffectiveDate,
                "The effective date cannot be in the past, because past target results must not change.");
        if (latest is not null && effectiveFrom < latest.EffectiveFrom)
            throw new OutletSetupException(
                SalesTargetErrorCodes.InvalidEffectiveDate,
                $"A saved configuration already starts on {latest.EffectiveFrom:yyyy-MM-dd}. Choose that date or a later one.");
    }

    private static async Task<(List<OutletSalesTargetOverride> Changed, List<OutletSalesTargetOverride> Cleared)> ApplyOverridesAsync(
        RetailPOSDbContext db,
        Outlet outlet,
        OutletSalesTargetInput input,
        OutletSalesTargetSchedule schedule,
        OutletSalesTargetVersion? added,
        OutletSalesTargetVersion? latestStored,
        long businessId,
        long? actorUserId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var changed = new List<OutletSalesTargetOverride>();
        var cleared = new List<OutletSalesTargetOverride>();
        if (input.Overrides is not { Count: > 0 })
            return (changed, cleared);

        var active = outlet.Id == 0
            ? []
            : await db.Set<OutletSalesTargetOverride>()
                .Where(item => item.OutletId == outlet.Id && item.ClearedAt == null)
                .ToListAsync(cancellationToken);

        foreach (var request in input.Overrides)
        {
            var (from, to) = SalesTargetRules.RequirePeriod(request);
            var current = active.FirstOrDefault(item => item.PeriodType == request.PeriodType && item.PeriodFrom == from);

            if (request.OverrideAmount is null)
            {
                if (current is not null)
                {
                    current.ClearedAt = now;
                    current.ClearedByUserId = actorUserId;
                    cleared.Add(current);
                }

                continue;
            }

            var calculated = SalesTargetCalculator.Period(schedule, from, to).Calculated
                ?? throw new OutletSetupException(
                    SalesTargetErrorCodes.InvalidOverride,
                    "Enable sales targets for this period before overriding it.");
            var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();

            if (current is null)
            {
                var created = new OutletSalesTargetOverride
                {
                    BusinessId = businessId,
                    PeriodType = request.PeriodType,
                    PeriodFrom = from,
                    PeriodTo = to,
                    CalculatedAmount = calculated,
                    OverrideAmount = request.OverrideAmount.Value,
                    Reason = reason,
                    UpdatedByUserId = actorUserId,
                    UpdatedAt = now
                };
                if (added is not null)
                    created.Version = added;
                else
                    created.VersionId = latestStored!.Id;
                outlet.SalesTargetOverrides.Add(created);
                changed.Add(created);
            }
            else if (current.OverrideAmount != request.OverrideAmount.Value
                     || current.Reason != reason
                     || current.CalculatedAmount != calculated)
            {
                current.CalculatedAmount = calculated;
                current.OverrideAmount = request.OverrideAmount.Value;
                current.Reason = reason;
                current.UpdatedByUserId = actorUserId;
                current.UpdatedAt = now;
                if (added is not null)
                    current.Version = added;
                changed.Add(current);
            }
        }

        return (changed, cleared);
    }

    private static SalesTargetVersionSnapshot ToSnapshot(OutletSalesTargetVersion version) =>
        new(
            version.VersionNo,
            version.EffectiveFrom,
            version.IsEnabled,
            version.Days
                .OrderBy(day => day.DayOfWeek)
                .Select(day => new SalesTargetDaySnapshot(day.DayOfWeek, day.IsOffDay, day.TargetAmount))
                .ToList());
}
