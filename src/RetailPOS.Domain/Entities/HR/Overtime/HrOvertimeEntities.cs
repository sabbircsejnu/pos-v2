using RetailPOS.Core.Entities.HR.Approvals;

namespace RetailPOS.Core.Entities.HR.Overtime;

public enum HrOvertimePolicyScopeType
{
    Business,
    LocationType,
    Location,
    Employee
}

public enum HrOvertimeRoundingMethod
{
    Down,
    Up,
    Nearest
}

public enum HrOvertimeRateType
{
    Multiplier,
    FixedHourly
}

public sealed class HrOvertimePolicyVersion
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public HrOvertimePolicyScopeType ScopeType { get; set; }
    public string? ScopeReference { get; set; }
    public int VersionNo { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public int MinimumEligibleMinutes { get; set; }
    public int? MaximumEligibleMinutes { get; set; }
    public int RoundingUnitMinutes { get; set; }
    public HrOvertimeRoundingMethod RoundingMethod { get; set; }
    public bool IncludePreShift { get; set; }
    public bool IncludePostShift { get; set; }
    public bool AllowNormalWorkingDay { get; set; }
    public bool AllowWeeklyOff { get; set; }
    public bool AllowHoliday { get; set; }
    public bool AllowSpecialDay { get; set; }
    public HrOvertimeRateType RateType { get; set; }
    public decimal NormalDayRate { get; set; }
    public decimal WeeklyOffRate { get; set; }
    public decimal HolidayRate { get; set; }
    public decimal SpecialDayRate { get; set; }
    public bool IsActive { get; set; }
    public long Version { get; set; } = 1;
}

public sealed class HrOvertimeEntry
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long EmployeeId { get; set; }
    public DateOnly WorkDate { get; set; }
    public long DailyAttendanceId { get; set; }
    public long OperationalLocationProjectionId { get; set; }
    public string DayCategory { get; set; } = "";
    public int AttendanceProcessingVersion { get; set; }
    public long PolicyVersionId { get; set; }
    public int PolicyVersionNo { get; set; }
    public int EligibleMinutes { get; set; }
    public int CalculatedMinutes { get; set; }
    public int? ApprovedMinutes { get; set; }
    public string Status { get; set; } = "Calculated";
    public int CalculationVersion { get; set; }
    public string CalculationFingerprint { get; set; } = "";
    public string RequestHash { get; set; } = "";
    public string PolicySnapshotJson { get; set; } = "";
    public string AttendanceSnapshotJson { get; set; } = "";
    public string CalculationInputsJson { get; set; } = "";
    public long? ApprovalInstanceId { get; set; }
    public long Version { get; set; } = 1;
    public HrOvertimePolicyVersion? PolicyVersion { get; set; }
    public ApprovalInstance? ApprovalInstance { get; set; }
    public List<HrOvertimeCalculationEvidence> CalculationEvidence { get; set; } = [];
}

public sealed class HrOvertimeCalculationEvidence
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long OvertimeEntryId { get; set; }
    public int CalculationVersion { get; set; }
    public int AttendanceProcessingVersion { get; set; }
    public long PolicyVersionId { get; set; }
    public int EligibleMinutes { get; set; }
    public int CalculatedMinutes { get; set; }
    public string DayCategory { get; set; } = "";
    public string CalculationFingerprint { get; set; } = "";
    public string PolicySnapshotJson { get; set; } = "";
    public string AttendanceSnapshotJson { get; set; } = "";
    public string CalculationInputsJson { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public long CreatedByUserId { get; set; }
}
