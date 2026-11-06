using RetailPOS.Application.Settings;
using RetailPOS.Application.Settings.Abstractions;

namespace RetailPOS.Application.Inventory.Queries;

/// <summary>
/// Turns a stock report's From/To into inclusive UTC instants. A date-only value, which is what the screens send, is
/// a Company-zone calendar date. A value that carries a zone keeps its long-standing API meaning: From is that
/// instant, and To runs through the end of its UTC date.
/// </summary>
internal static class StockReportDateWindow
{
    public static async Task<(DateTime? From, DateTime? To)> ResolveAsync(
        DateTime? from,
        DateTime? to,
        ICurrentCompanyTimeZone timeZones,
        CancellationToken cancellationToken)
    {
        if (!from.HasValue && !to.HasValue)
            return (null, null);

        var timeZoneId = await timeZones.GetTimeZoneIdAsync(cancellationToken);
        return (
            from.HasValue ? CompanyDates.StartUtc(from.Value, timeZoneId) : null,
            to.HasValue ? EndUtc(to.Value, timeZoneId) : null);
    }

    private static DateTime EndUtc(DateTime to, string timeZoneId) =>
        to.Kind == DateTimeKind.Unspecified
            ? CompanyDates.DayWindow(null, DateOnly.FromDateTime(to), timeZoneId).ToExclusiveUtc!.Value.AddTicks(-1)
            : to.ToUniversalTime().Date.AddDays(1).AddTicks(-1);
}
