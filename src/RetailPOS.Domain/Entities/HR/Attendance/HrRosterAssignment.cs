namespace RetailPOS.Core.Entities.HR.Attendance;

public sealed class HrRosterAssignment
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long EmployeeId { get; set; }
    public DateOnly WorkDate { get; set; }
    public long ShiftId { get; set; }
    public long OperationalLocationProjectionId { get; set; }
    public string Source { get; set; } = "Manual";
    public string WorkKind { get; set; } = "Normal";
    public string DayClassification { get; set; } = "NormalWorkingDay";
    public string Status { get; set; } = "Draft";
    public long Version { get; set; } = 1;
    public long? PublishedByUserId { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
    public string? ShiftSnapshotJson { get; set; }
    public string? LocationSnapshotJson { get; set; }
    public string? CalendarEvidenceJson { get; set; }
}

public static class HrRosterRules
{
    public static void Validate(DateOnly date, long employeeId, long shiftId, long locationId,
        string source, string workKind, string classification)
    {
        if (date == default || date == DateOnly.MaxValue || employeeId <= 0 || shiftId <= 0 || locationId <= 0)
            throw new ArgumentException("Employee, WorkDate, shift and HR location are required.");
        if (source is not ("Manual" or "Bulk" or "Template")
            || workKind is not ("Normal" or "Extended" or "Special")
            || classification is not ("NormalWorkingDay" or "Holiday" or "WeeklyOff"))
            throw new ArgumentException("Roster source, work kind or day classification is invalid.");
    }
}
