using RetailPOS.Application.HR.Calendars;
using RetailPOS.Core.Entities.HR.Calendars;

namespace RetailPOS.Application.HR.Attendance;

public sealed record HrAttendanceSchedule(DateOnly WorkDate, HrShiftData Shift, string DayClassification);
public sealed record HrAttendancePunchSample(long Id, DateTime PunchTimeLocal, DateTimeOffset? PunchTimeUtc,
    string? TimeZoneId, string? DirectionCode);
public sealed record HrAttendanceCalculation(DateTime? FirstInAtLocal, DateTime? LastOutAtLocal,
    int WorkedMinutes, int BreakMinutes, int LateMinutes, int EarlyLeaveMinutes, string Status,
    IReadOnlyList<long> PunchIds, string? TimeZoneIdSnapshot);

public static class HrAttendanceProcessingRules
{
    public static HrAttendanceCalculation Calculate(HrAttendanceSchedule schedule,
        IReadOnlyList<HrAttendancePunchSample> candidates)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(schedule.Shift);
        ArgumentNullException.ThrowIfNull(candidates);
        if (schedule.WorkDate == default || schedule.WorkDate == DateOnly.MaxValue
            || schedule.DayClassification is not ("NormalWorkingDay" or "Holiday" or "WeeklyOff"))
            throw new ArgumentException("Published WorkDate and classification are invalid.");

        var shift = schedule.Shift;
        var duration = HrShiftRules.DurationMinutes(shift.StartTimeLocal, shift.EndTimeLocal, shift.CrossesMidnight);
        var start = schedule.WorkDate.ToDateTime(shift.StartTimeLocal, DateTimeKind.Unspecified);
        var end = start.AddMinutes(duration);
        var from = start.AddMinutes(-shift.AllowedEarlyPunchMinutes);
        var to = end.AddMinutes(shift.AllowedLatePunchMinutes);
        var punches = candidates.Where(p => p.PunchTimeUtc.HasValue
                && p.PunchTimeLocal >= from && p.PunchTimeLocal <= to)
            .OrderBy(p => p.PunchTimeLocal).ThenBy(p => p.Id).ToArray();
        var zoneIds = punches.Select(p => p.TimeZoneId).Where(z => !string.IsNullOrWhiteSpace(z))
            .Distinct(StringComparer.Ordinal).ToArray();
        if (zoneIds.Length > 1)
            throw new InvalidOperationException("Punches from different time zones cannot be paired against one published local shift.");
        var zoneSnapshot = zoneIds.SingleOrDefault();
        if (punches.Length == 0)
            return new(null, null, 0, 0, 0, 0, schedule.DayClassification switch
            {
                "Holiday" => "Holiday",
                "WeeklyOff" => "WeeklyOff",
                _ => "Absent"
            }, [], zoneSnapshot);

        var hasCanonicalDirection = punches.Any(p => IsDirection(p, "IN") || IsDirection(p, "OUT"));
        var first = hasCanonicalDirection
            ? punches.FirstOrDefault(p => IsDirection(p, "IN"))
            : punches[0];
        if (first is null)
            return new(null, punches[^1].PunchTimeLocal, 0, 0, 0, 0,
                "MissingPunch", punches.Select(p => p.Id).ToArray(), zoneSnapshot);
        if (punches.Length == 1)
        {
            var isOutOnly = IsDirection(first, "OUT");
            return new(isOutOnly ? null : first.PunchTimeLocal, isOutOnly ? first.PunchTimeLocal : null,
                0, 0, isOutOnly ? 0 : LateMinutes(first.PunchTimeLocal, start, shift.GraceInMinutes),
                0, "MissingPunch", [first.Id], zoneSnapshot);
        }

        var directedOut = punches.LastOrDefault(p => IsDirection(p, "OUT") && p.PunchTimeLocal > first.PunchTimeLocal);
        var trailingIn = punches.LastOrDefault(p => IsDirection(p, "IN") && p.PunchTimeLocal > first.PunchTimeLocal);
        var fallbackOut = hasCanonicalDirection ? null
            : punches.LastOrDefault(p => p.PunchTimeLocal > first.PunchTimeLocal);
        var last = directedOut ?? fallbackOut;
        if (trailingIn is not null && (last is null || trailingIn.PunchTimeLocal > last.PunchTimeLocal))
            return new(first.PunchTimeLocal, null, 0, 0, LateMinutes(first.PunchTimeLocal, start, shift.GraceInMinutes),
                0, "MissingPunch", punches.Select(p => p.Id).ToArray(), zoneSnapshot);
        if (last is null)
        {
            return new(first.PunchTimeLocal, null, 0, 0, LateMinutes(first.PunchTimeLocal, start, shift.GraceInMinutes),
                0, "MissingPunch", punches.Select(p => p.Id).ToArray(), zoneSnapshot);
        }

        var maximum = shift.MaximumAllowedWorkMinutes ?? duration + shift.AllowedEarlyPunchMinutes + shift.AllowedLatePunchMinutes;
        if ((last.PunchTimeLocal - first.PunchTimeLocal).TotalMinutes > maximum)
        {
            var boundedOut = punches.LastOrDefault(p => p.PunchTimeLocal > first.PunchTimeLocal
                && (p.PunchTimeLocal - first.PunchTimeLocal).TotalMinutes <= maximum
                && !IsDirection(p, "IN"));
            if (boundedOut is null)
                return new(first.PunchTimeLocal, null, 0, 0, LateMinutes(first.PunchTimeLocal, start, shift.GraceInMinutes),
                    0, "MissingPunch", [first.Id], zoneSnapshot);
            last = boundedOut;
        }

        var gross = Math.Max(0, (int)Math.Floor((last.PunchTimeLocal - first.PunchTimeLocal).TotalMinutes));
        var unpaidBreak = shift.Breaks.Where(b => !b.IsPaid).Sum(b => b.DurationMinutes);
        var breakMinutes = Math.Min(gross, unpaidBreak);
        var worked = gross - breakMinutes;
        var late = LateMinutes(first.PunchTimeLocal, start, shift.GraceInMinutes);
        var earlyLeave = EarlyLeaveMinutes(last.PunchTimeLocal, end, shift.GraceOutMinutes);
        var status = worked < shift.MinimumWorkMinutes ? "HalfDay" : late > 0 ? "Late" : "Present";
        var usedIds = punches.Where(p => p.PunchTimeLocal >= first.PunchTimeLocal
                && p.PunchTimeLocal <= last.PunchTimeLocal)
            .Select(p => p.Id).ToArray();
        return new(first.PunchTimeLocal, last.PunchTimeLocal, worked, breakMinutes, late,
            earlyLeave, status, usedIds, zoneSnapshot);
    }

    private static bool IsDirection(HrAttendancePunchSample punch, string value) =>
        string.Equals(punch.DirectionCode, value, StringComparison.OrdinalIgnoreCase);

    private static int LateMinutes(DateTime firstIn, DateTime shiftStart, int grace) =>
        Math.Max(0, (int)Math.Ceiling((firstIn - shiftStart.AddMinutes(grace)).TotalMinutes));

    private static int EarlyLeaveMinutes(DateTime lastOut, DateTime shiftEnd, int grace) =>
        Math.Max(0, (int)Math.Ceiling((shiftEnd.AddMinutes(-grace) - lastOut).TotalMinutes));
}
