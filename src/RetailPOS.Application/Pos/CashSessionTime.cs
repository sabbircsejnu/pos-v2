using RetailPOS.Core.Entities;

namespace RetailPOS.Application.Pos;

public enum CashSessionWarningLevel
{
    None = 0,

    /// <summary>Two hours (or less) before the cutoff, e.g. 18 h of a 20 h session.</summary>
    Warning = 1,

    /// <summary>One hour (or less) before the cutoff, e.g. 19 h of a 20 h session.</summary>
    Urgent = 2,

    /// <summary>The cutoff has been reached: sales are rejected, closing is still permitted.</summary>
    Overdue = 3
}

/// <summary>D3/D4/D9 rules that do not depend on the host machine's time zone or clock.</summary>
public static class CashSessionTime
{
    public static readonly TimeSpan WarningBeforeCutoff = TimeSpan.FromHours(2);
    public static readonly TimeSpan UrgentBeforeCutoff = TimeSpan.FromHours(1);

    /// <summary>The Outlet override when configured, otherwise the Business default (D9).</summary>
    public static string ResolveEffectiveTimeZoneId(string? outletOverride, string businessDefault)
    {
        var effective = string.IsNullOrWhiteSpace(outletOverride) ? businessDefault : outletOverride;
        if (string.IsNullOrWhiteSpace(effective))
            effective = BusinessPosSettings.DefaultTimeZone;
        return effective.Trim();
    }

    public static TimeZoneInfo FindZone(string timeZoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId.Trim());
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new CashSessionException(
                CashSessionErrorCodes.TimeZoneInvalid,
                $"'{timeZoneId}' is not a recognised IANA time zone.");
        }
    }

    /// <summary>The local calendar date, in <paramref name="timeZoneId"/>, of the UTC instant (the session BusinessDate).</summary>
    public static DateTime BusinessDateFor(DateTime utcInstant, string timeZoneId)
    {
        var zone = FindZone(timeZoneId);
        return TimeZoneInfo.ConvertTimeFromUtc(UtcTime.Normalize(utcInstant), zone).Date;
    }

    public static DateTime CutoffUtc(DateTime openedAtUtc, int durationHours) =>
        UtcTime.Normalize(openedAtUtc).AddHours(durationHours);

    public static bool IsOverdue(DateTime openedAtUtc, int durationHours, DateTime nowUtc) =>
        UtcTime.Normalize(nowUtc) >= CutoffUtc(openedAtUtc, durationHours);

    public static CashSessionWarningLevel WarningLevel(DateTime openedAtUtc, int durationHours, DateTime nowUtc)
    {
        var remaining = CutoffUtc(openedAtUtc, durationHours) - UtcTime.Normalize(nowUtc);
        if (remaining <= TimeSpan.Zero) return CashSessionWarningLevel.Overdue;
        if (remaining <= UrgentBeforeCutoff) return CashSessionWarningLevel.Urgent;
        if (remaining <= WarningBeforeCutoff) return CashSessionWarningLevel.Warning;
        return CashSessionWarningLevel.None;
    }

    public static long ElapsedSeconds(DateTime openedAtUtc, DateTime nowUtc) =>
        Math.Max(0, (long)(UtcTime.Normalize(nowUtc) - UtcTime.Normalize(openedAtUtc)).TotalSeconds);
}
