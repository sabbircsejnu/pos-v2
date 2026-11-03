namespace RetailPOS.Core.Entities.HR.Attendance;

public sealed class HrDailyAttendance
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long EmployeeId { get; set; }
    public DateOnly WorkDate { get; set; }
    public long OperationalLocationProjectionId { get; set; }
    public string SourceLocationTypeSnapshot { get; set; } = "";
    public long ShiftId { get; set; }
    public long RosterAssignmentId { get; set; }
    public string DayClassification { get; set; } = "NormalWorkingDay";
    public string? TimeZoneIdSnapshot { get; set; }
    public DateTime? FirstInAtLocal { get; set; }
    public DateTime? LastOutAtLocal { get; set; }
    public int WorkedMinutes { get; set; }
    public int BreakMinutes { get; set; }
    public int LateMinutes { get; set; }
    public int EarlyLeaveMinutes { get; set; }
    public int OvertimeMinutesCalculated { get; set; }
    public string Status { get; set; } = "Absent";
    public int ProcessingVersion { get; set; } = 1;
    public DateTime LastProcessedAtUtc { get; set; }
    public bool IsManuallyAdjusted { get; set; }
    public string ShiftSnapshotJson { get; set; } = "";
    public string LocationSnapshotJson { get; set; } = "";
    public string CalendarEvidenceJson { get; set; } = "";
    public string PunchIdsJson { get; set; } = "[]";
    public string CalculationFingerprint { get; set; } = "";
}
