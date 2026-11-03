namespace RetailPOS.Core.Entities.HR.Calendars;

public sealed class HrShift
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public TimeOnly StartTimeLocal { get; set; }
    public TimeOnly EndTimeLocal { get; set; }
    public bool CrossesMidnight { get; set; }
    public int MinimumWorkMinutes { get; set; }
    public int? MaximumAllowedWorkMinutes { get; set; }
    public int AllowedEarlyPunchMinutes { get; set; }
    public int AllowedLatePunchMinutes { get; set; }
    public int? MinimumRestBeforeNextShiftMinutes { get; set; }
    public int GraceInMinutes { get; set; }
    public int GraceOutMinutes { get; set; }
    public bool OvertimeEligible { get; set; }
    public bool IsActive { get; set; } = true;
    public long Version { get; set; } = 1;
    public List<HrShiftBreak> Breaks { get; set; } = [];
}

public sealed class HrShiftBreak
{
    public long Id { get; set; }
    public long ShiftId { get; set; }
    public int Sequence { get; set; }
    public int DurationMinutes { get; set; }
    public bool IsPaid { get; set; }
}

public sealed class HrHolidayCalendar
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public string Name { get; set; } = "";
    public string ScopeType { get; set; } = "Company";
    public string? LocationType { get; set; }
    public long? OperationalLocationProjectionId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsActive { get; set; } = true;
    public long Version { get; set; } = 1;
}

public sealed class HrHoliday
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long CalendarId { get; set; }
    public DateOnly HolidayDate { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string HolidayType { get; set; } = "";
    public bool IsPaid { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public long Version { get; set; } = 1;
}

public sealed class HrWeeklyOffPolicyVersion
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public string ScopeKey { get; set; } = "";
    public string ScopeType { get; set; } = "Company";
    public string? LocationType { get; set; }
    public long? OperationalLocationProjectionId { get; set; }
    public int VersionNo { get; set; } = 1;
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsActive { get; set; } = true;
    public long Version { get; set; } = 1;
    public List<HrWeeklyOffPolicyDay> Days { get; set; } = [];
}

public sealed class HrWeeklyOffPolicyDay
{
    public long Id { get; set; }
    public long PolicyVersionId { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
}
