using RetailPOS.Application.Pos;

namespace RetailPOS.Application.Settings;

/// <summary>
/// Calendar dates in a Business's Company time zone. Timestamps stay in UTC; these helpers derive the date a
/// user sees, filters on and posts to, so the three always agree.
/// </summary>
public static class CompanyDates
{
    /// <summary>Inclusive local From/To dates as a UTC range whose upper bound is the next local day's start, exclusive.</summary>
    public static (DateTime? FromUtc, DateTime? ToExclusiveUtc) DayWindow(
        DateOnly? fromDate,
        DateOnly? toDate,
        string timeZoneId)
    {
        var zone = CashSessionTime.FindZone(timeZoneId);
        return (
            fromDate.HasValue ? LocalDayStartUtc(fromDate.Value, zone) : null,
            toDate.HasValue ? LocalDayStartUtc(toDate.Value.AddDays(1), zone) : null);
    }

    /// <summary>The Company-zone calendar date of a UTC instant, as a midnight value for date columns.</summary>
    public static DateTime DateFor(DateTime instantUtc, string timeZoneId) =>
        DateTime.SpecifyKind(CashSessionTime.BusinessDateFor(instantUtc, timeZoneId), DateTimeKind.Utc);

    /// <summary>
    /// The calendar date a value means in the Company zone. A value without a zone is already a calendar date
    /// (a date picker); anything else is an instant.
    /// </summary>
    public static DateOnly CalendarDateOf(DateTime value, string timeZoneId) =>
        value.Kind == DateTimeKind.Unspecified
            ? DateOnly.FromDateTime(value)
            : DateOnly.FromDateTime(DateFor(value.ToUniversalTime(), timeZoneId));

    /// <summary>The start of a range: the first instant of a calendar date, or the instant itself.</summary>
    public static DateTime StartUtc(DateTime value, string timeZoneId) =>
        value.Kind == DateTimeKind.Unspecified
            ? DayWindow(DateOnly.FromDateTime(value), null, timeZoneId).FromUtc!.Value
            : value.ToUniversalTime();

    /// <summary>The end of a range: the last instant of a calendar date, or the instant itself.</summary>
    public static DateTime EndInclusiveUtc(DateTime value, string timeZoneId) =>
        value.Kind == DateTimeKind.Unspecified
            ? DayWindow(null, DateOnly.FromDateTime(value), timeZoneId).ToExclusiveUtc!.Value.AddTicks(-1)
            : value.ToUniversalTime();

    /// <summary>The first UTC instant of a local day, including days whose midnight a DST change skips or repeats.</summary>
    private static DateTime LocalDayStartUtc(DateOnly date, TimeZoneInfo zone)
    {
        var midnight = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var offset = zone.IsInvalidTime(midnight)
            ? zone.GetUtcOffset(midnight.AddDays(-1))
            : zone.IsAmbiguousTime(midnight)
                ? zone.GetAmbiguousTimeOffsets(midnight).Max()
                : zone.GetUtcOffset(midnight);
        return DateTime.SpecifyKind(midnight - offset, DateTimeKind.Utc);
    }
}
