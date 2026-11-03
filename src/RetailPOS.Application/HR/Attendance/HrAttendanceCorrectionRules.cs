using RetailPOS.Application.HR.Calendars;

namespace RetailPOS.Application.HR.Attendance;

public sealed record HrAttendanceCorrectionValues(
    DateTime? FirstInAtLocal,
    DateTime? LastOutAtLocal,
    int WorkedMinutes,
    int BreakMinutes,
    int LateMinutes,
    int EarlyLeaveMinutes,
    string Status);

public sealed record HrAttendanceCorrectionTargetPayload(
    Guid CorrectionId,
    long DailyAttendanceId,
    int ExpectedProcessingVersion,
    HrAttendanceCorrectionValues Values,
    string Reason);

public sealed record HrAttendanceCorrectionEvidence(
    long DailyAttendanceId,
    long BusinessId,
    long EmployeeId,
    DateOnly WorkDate,
    long OperationalLocationProjectionId,
    long ShiftId,
    long RosterAssignmentId,
    string DayClassification,
    string? TimeZoneIdSnapshot,
    HrAttendanceCorrectionValues Values,
    int OvertimeMinutesCalculated,
    int ProcessingVersion,
    bool IsManuallyAdjusted,
    IReadOnlyList<long> PunchIds,
    string ShiftSnapshotJson,
    string LocationSnapshotJson,
    string CalendarEvidenceJson);

public static class HrAttendanceCorrectionRules
{
    private static readonly HashSet<string> AllowedStatuses =
        ["Present", "Absent", "Late", "HalfDay", "Leave", "Holiday", "WeeklyOff", "MissingPunch"];

    public static void ValidateShape(Guid correctionId, long dailyAttendanceId, int expectedProcessingVersion,
        HrAttendanceCorrectionValues values, string? reason)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (correctionId == Guid.Empty || dailyAttendanceId <= 0 || expectedProcessingVersion <= 0)
            throw new ArgumentException("A correction ID, attendance ID and expected processing version are required.");
        if (!AllowedStatuses.Contains(values.Status))
            throw new ArgumentException("Attendance correction status is not supported.");
        if (values.WorkedMinutes < 0 || values.BreakMinutes < 0 || values.LateMinutes < 0
            || values.EarlyLeaveMinutes < 0)
            throw new ArgumentException("Attendance correction minutes cannot be negative.");
        if (values.FirstInAtLocal is { Kind: not DateTimeKind.Unspecified }
            || values.LastOutAtLocal is { Kind: not DateTimeKind.Unspecified })
            throw new ArgumentException("Corrected local timestamps must be unspecified wall-clock values.");
        if (values.FirstInAtLocal.HasValue != values.LastOutAtLocal.HasValue
            && values.Status != "MissingPunch")
            throw new ArgumentException("A partial corrected punch pair must use MissingPunch status.");
        if (values.FirstInAtLocal is null && values.LastOutAtLocal is null
            && (values.WorkedMinutes != 0 || values.BreakMinutes != 0))
            throw new ArgumentException("Worked and break minutes require a corrected punch pair.");
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500)
            throw new ArgumentException("A correction reason of 1 to 500 characters is required.");
    }

    public static void ValidateAgainstShift(
        DateOnly workDate,
        HrShiftData shift,
        HrAttendanceCorrectionValues values)
    {
        ArgumentNullException.ThrowIfNull(shift);
        ArgumentNullException.ThrowIfNull(values);
        var duration = (shift.EndTimeLocal.ToTimeSpan() - shift.StartTimeLocal.ToTimeSpan()).TotalMinutes;
        if (shift.CrossesMidnight)
            duration += TimeSpan.FromDays(1).TotalMinutes;
        if (duration <= 0)
            throw new InvalidOperationException("Frozen shift snapshot has an invalid duration.");

        var shiftStart = workDate.ToDateTime(shift.StartTimeLocal, DateTimeKind.Unspecified);
        var shiftEnd = shiftStart.AddMinutes(duration);
        var earliest = shiftStart.AddMinutes(-shift.AllowedEarlyPunchMinutes);
        var latest = shiftEnd.AddMinutes(shift.AllowedLatePunchMinutes);
        foreach (var punch in new[] { values.FirstInAtLocal, values.LastOutAtLocal }.OfType<DateTime>())
        {
            if (punch < earliest || punch > latest)
                throw new ArgumentException("Corrected punch timestamp falls outside the frozen shift punch window.");
        }

        if (values.FirstInAtLocal.HasValue && values.LastOutAtLocal.HasValue)
        {
            var grossMinutes = (values.LastOutAtLocal.Value - values.FirstInAtLocal.Value).TotalMinutes;
            var maximum = shift.MaximumAllowedWorkMinutes
                ?? duration + shift.AllowedEarlyPunchMinutes + shift.AllowedLatePunchMinutes;
            if (grossMinutes <= 0 || grossMinutes > maximum)
                throw new ArgumentException("Corrected punch duration is invalid or exceeds the frozen shift maximum.");
            if (values.BreakMinutes > grossMinutes || values.WorkedMinutes + values.BreakMinutes > grossMinutes)
                throw new ArgumentException("Corrected work and break minutes exceed the corrected punch duration.");
        }
        else if (values.WorkedMinutes != 0 || values.BreakMinutes != 0)
            throw new ArgumentException("Incomplete corrected punches cannot claim worked or break minutes.");

        if (values.LateMinutes > duration + shift.AllowedEarlyPunchMinutes
            || values.EarlyLeaveMinutes > duration + shift.AllowedLatePunchMinutes)
            throw new ArgumentException("Corrected late or early-leave minutes exceed the frozen shift window.");
    }
}
