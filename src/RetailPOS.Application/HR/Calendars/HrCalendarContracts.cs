using RetailPOS.Application.HR.Organisation;

namespace RetailPOS.Application.HR.Calendars;

/// <summary>A duration-based paid or unpaid break, ordered within one shift.</summary>
public sealed record HrShiftBreakData(int Sequence, int DurationMinutes, bool IsPaid);
/// <summary>A Business shift definition in local wall-clock time; WorkDate is its start date.</summary>
public sealed record HrShiftData(string Code, string Name, TimeOnly StartTimeLocal, TimeOnly EndTimeLocal,
    bool CrossesMidnight, int MinimumWorkMinutes, int? MaximumAllowedWorkMinutes,
    int AllowedEarlyPunchMinutes, int AllowedLatePunchMinutes, int? MinimumRestBeforeNextShiftMinutes,
    int GraceInMinutes, int GraceOutMinutes, bool OvertimeEligible, IReadOnlyList<HrShiftBreakData> Breaks);
/// <summary>A shift with optimistic concurrency metadata.</summary>
public sealed record HrShiftResponse(long Id, long BusinessId, HrShiftData Data, bool IsActive, long Version);
/// <summary>Company, supported location type, or one HR operational location scope.</summary>
public sealed record HrCalendarScope(string ScopeType, string? LocationType = null,
    long? OperationalLocationProjectionId = null);
/// <summary>A holiday calendar and its inclusive effective period.</summary>
public sealed record HrHolidayCalendarData(string Name, HrCalendarScope Scope,
    DateOnly EffectiveFrom, DateOnly? EffectiveTo);
/// <summary>A holiday calendar with concurrency metadata.</summary>
public sealed record HrHolidayCalendarResponse(long Id, long BusinessId, HrHolidayCalendarData Data,
    bool IsActive, long Version);
/// <summary>A named holiday; rate calculation belongs to a later overtime policy.</summary>
public sealed record HrHolidayData(DateOnly HolidayDate, string Code, string Name, string HolidayType,
    bool IsPaid, string? Notes);
/// <summary>A holiday with concurrency metadata.</summary>
public sealed record HrHolidayResponse(long Id, long CalendarId, HrHolidayData Data, bool IsActive, long Version);
/// <summary>A recurring weekly-off policy version with an inclusive effective period.</summary>
public sealed record HrWeeklyOffData(HrCalendarScope Scope, DateOnly EffectiveFrom,
    DateOnly? EffectiveTo, IReadOnlyList<DayOfWeek> Days);
/// <summary>A retained effective-dated weekly-off version.</summary>
public sealed record HrWeeklyOffResponse(long Id, long BusinessId, int VersionNo, HrWeeklyOffData Data,
    bool IsActive, long Version);
/// <summary>Day-classification evidence; overlapping benefits are not calculated here.</summary>
public sealed record HrDayClassificationResponse(DateOnly WorkDate, string Classification, string Source,
    bool IsHoliday, bool IsWeeklyOff, IReadOnlyList<HrHolidayResponse> Holidays,
    HrWeeklyOffResponse? WeeklyOffPolicy);

public interface IHrCalendarStore
{
    Task<IReadOnlyList<HrShiftResponse>> ShiftsAsync(long businessId, string? search, CancellationToken ct);
    Task<HrShiftResponse> SaveShiftAsync(long businessId, long? id, long? expectedVersion, HrShiftData? data, CancellationToken ct);
    Task<IReadOnlyList<HrHolidayCalendarResponse>> CalendarsAsync(long businessId, HrOperationalLocationReadScope scope, CancellationToken ct);
    Task<HrHolidayCalendarResponse> SaveCalendarAsync(long businessId, long? id, long? expectedVersion,
        HrHolidayCalendarData? data, DateOnly? effectiveTo, CancellationToken ct);
    Task<IReadOnlyList<HrHolidayResponse>> HolidaysAsync(long businessId, long calendarId, CancellationToken ct);
    Task<HrHolidayResponse> SaveHolidayAsync(long businessId, long calendarId, long? id, long? expectedVersion,
        HrHolidayData? data, CancellationToken ct);
    Task<IReadOnlyList<HrWeeklyOffResponse>> WeeklyOffAsync(long businessId, HrOperationalLocationReadScope scope, CancellationToken ct);
    Task<HrWeeklyOffResponse> SaveWeeklyOffAsync(long businessId, long? previousId, long? expectedVersion,
        HrWeeklyOffData? data, DateOnly? effectiveTo, CancellationToken ct);
    Task<HrCalendarScope> CalendarScopeAsync(long businessId, long id, CancellationToken ct);
    Task<HrCalendarScope> WeeklyOffScopeAsync(long businessId, long id, CancellationToken ct);
    Task<HrDayClassificationResponse> ClassifyAsync(long businessId, DateOnly date, long locationId, CancellationToken ct);
}

public sealed class HrCalendarConflictException(string message, Exception? inner = null) : Exception(message, inner);
