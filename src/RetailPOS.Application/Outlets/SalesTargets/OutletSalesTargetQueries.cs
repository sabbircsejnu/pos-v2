using MediatR;
using RetailPOS.Application.Outlets.Abstractions;
using RetailPOS.Application.Settings.Abstractions;

namespace RetailPOS.Application.Outlets.SalesTargets;

/// <summary>Saved configuration, history and the four calendar periods containing the reference date.</summary>
public sealed record GetOutletSalesTargetsQuery(long OutletId, DateOnly? ReferenceDate = null)
    : IRequest<OutletSalesTargetConfigurationReadModel?>;

public sealed class GetOutletSalesTargetsQueryHandler(
    IOutletSalesTargetReadStore store,
    IOutletScopeProvider scopeProvider,
    ICompanyTimeZoneProvider timeZones)
    : IRequestHandler<GetOutletSalesTargetsQuery, OutletSalesTargetConfigurationReadModel?>
{
    public async Task<OutletSalesTargetConfigurationReadModel?> Handle(
        GetOutletSalesTargetsQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = await scopeProvider.GetBusinessIdAsync(cancellationToken);
        var stored = await store.GetConfigurationAsync(businessId, request.OutletId, cancellationToken);
        if (stored is null)
            return null;

        // A platform user has no Business of their own, so the outlet's Business decides the date.
        var today = stored.BusinessId is long outletBusinessId
            ? DateOnly.FromDateTime(await timeZones.GetTodayAsync(outletBusinessId, cancellationToken))
            : DateOnly.FromDateTime(DateTime.UtcNow);
        var reference = request.ReferenceDate ?? today;
        var schedule = new OutletSalesTargetSchedule(
            stored.OutletId,
            stored.Versions
                .Select(version => new SalesTargetVersionSnapshot(
                    version.VersionNo, version.EffectiveFrom, version.IsEnabled, version.Days))
                .ToList(),
            stored.ActiveOverrides
                .Select(item => new SalesTargetOverrideSnapshot(
                    item.PeriodType, item.PeriodFrom, item.PeriodTo, item.OverrideAmount))
                .ToList());

        var periods = SalesTargetPeriodTypes.All
            .Select(type =>
            {
                var (from, to) = SalesTargetCalculator.RangeOf(type, reference);
                var result = SalesTargetCalculator.Period(schedule, from, to);
                var match = stored.ActiveOverrides.FirstOrDefault(item => item.PeriodFrom == from && item.PeriodTo == to);
                return new OutletSalesTargetPeriodReadModel(
                    type,
                    from,
                    to,
                    result.Calculated,
                    result.Target,
                    match?.OverrideAmount,
                    result.IsOverridden,
                    match?.Reason,
                    match?.UpdatedByUserId,
                    match?.UpdatedAt,
                    result.WorkingDays,
                    result.OffDays,
                    result.NotConfiguredDays);
            })
            .ToList();

        var current = SalesTargetCalculator.VersionOn(schedule, today);
        var latest = stored.Versions
            .OrderByDescending(version => version.VersionNo)
            .FirstOrDefault();

        return new OutletSalesTargetConfigurationReadModel(
            stored.OutletId,
            today,
            latest,
            current is null ? null : stored.Versions.First(version => version.VersionNo == current.VersionNo),
            periods,
            stored.Versions.OrderByDescending(version => version.VersionNo).ToList());
    }
}
