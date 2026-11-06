using RetailPOS.Application.Outlets.SalesTargets;

namespace RetailPOS.Application.OwnerDashboards;

/// <summary>One showroom's target for a range, with only the sales that can fairly be compared to it.</summary>
public sealed record ShowroomTargetFigures(
    long OutletId,
    SalesTargetPeriodResult Period,
    decimal ComparableSales,
    string Status);

/// <summary>How much of the selected scope has a target, so a partial total is never read as a whole one.</summary>
public sealed record SalesTargetCoverage(
    int ShowroomCount,
    int FullyConfiguredShowrooms,
    int PartlyConfiguredShowrooms,
    int NotConfiguredShowrooms,
    int ScheduledOffShowrooms,
    bool IsComplete);

public sealed record AggregatedSalesTarget(
    decimal? Target,
    decimal? AchievementPercent,
    decimal? Shortfall,
    decimal? Exceeded,
    string Status,
    string? Note,
    SalesTargetCoverage Coverage);

/// <summary>
/// Compares targets with sales, one showroom at a time and then in total. Off-day sales always count as sales, against a
/// zero target for that day. Sales on days with no target are left out of the comparison, because nothing was asked of them.
/// </summary>
public static class SalesTargetPerformance
{
    public const string NotConfiguredNote = "No sales target is configured for this selection.";
    public const string OffDayNote = "Scheduled off day: the target is zero and no shortfall applies.";

    public static ShowroomTargetFigures ForShowroom(
        OutletSalesTargetSchedule schedule,
        DateOnly from,
        DateOnly to,
        IEnumerable<SalesDayFact> days)
    {
        var period = SalesTargetCalculator.Period(schedule, from, to);
        var sales = days.Where(day => day.OutletId == schedule.OutletId && day.BusinessDate >= from && day.BusinessDate <= to);
        var comparable = period.IsOverridden
            ? sales.Sum(day => day.NetSales)
            : sales
                .Where(day => SalesTargetCalculator.Day(schedule, day.BusinessDate).Kind != SalesTargetDayKind.NotConfigured)
                .Sum(day => day.NetSales);
        return new ShowroomTargetFigures(schedule.OutletId, period, comparable, StatusOf(period));
    }

    public static string StatusOf(SalesTargetPeriodResult period)
    {
        if (period.Target is null)
            return MetricStatuses.NotConfigured;
        if (period.IsOverridden)
            return MetricStatuses.Ok;
        if (period.NotConfiguredDays > 0)
            return MetricStatuses.Partial;
        return period.WorkingDays == 0 ? MetricStatuses.OffDay : MetricStatuses.Ok;
    }

    /// <summary>The outcome for one figure. An off day has no shortfall, and a zero target has no percentage.</summary>
    public static TargetOutcome Evaluate(decimal comparableSales, decimal? target, string status) =>
        status is MetricStatuses.NotConfigured or MetricStatuses.OffDay
            ? new TargetOutcome(null, null, null, status)
            : SalesFinanceMath.EvaluateTarget(comparableSales, target) with { Status = status };

    public static AggregatedSalesTarget Aggregate(IReadOnlyList<ShowroomTargetFigures> showrooms)
    {
        var configured = showrooms.Where(item => item.Period.Target is not null).ToList();
        var notConfigured = showrooms.Count - configured.Count;
        var partly = configured.Count(item => item.Status == MetricStatuses.Partial);
        var off = configured.Count(item => item.Status == MetricStatuses.OffDay);
        var coverage = new SalesTargetCoverage(
            showrooms.Count,
            configured.Count - partly,
            partly,
            notConfigured,
            off,
            showrooms.Count > 0 && notConfigured == 0 && partly == 0);

        if (configured.Count == 0)
            return new AggregatedSalesTarget(null, null, null, null, MetricStatuses.NotConfigured, NotConfiguredNote, coverage);

        var target = configured.Sum(item => item.Period.Target!.Value);
        var comparable = configured.Sum(item => item.ComparableSales);
        var status = !coverage.IsComplete
            ? MetricStatuses.Partial
            : off == configured.Count ? MetricStatuses.OffDay : MetricStatuses.Ok;
        var outcome = Evaluate(comparable, target, status);
        return new AggregatedSalesTarget(
            target,
            outcome.AchievementPercent,
            outcome.Shortfall,
            outcome.Exceeded,
            status,
            status switch
            {
                MetricStatuses.Partial => DescribePartial(coverage),
                MetricStatuses.OffDay => OffDayNote,
                _ => null
            },
            coverage);
    }

    private static string DescribePartial(SalesTargetCoverage coverage) =>
        $"Incomplete: {coverage.FullyConfiguredShowrooms} of {coverage.ShowroomCount} showroom(s) have a target for the whole period. "
        + "Achievement and shortfall compare only the sales on days that have a target.";
}
