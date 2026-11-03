using MediatR;
using System.Text.Json;
using RetailPOS.Application.HR.Calendars;
using RetailPOS.Application.HR.Organisation;

namespace RetailPOS.Application.HR.Attendance;

/// <summary>Filters and paging options for the attendance report.</summary>
public sealed record HrAttendanceReportQuery(
    DateOnly From,
    DateOnly To,
    long? EmployeeId = null,
    long? LocationProjectionId = null,
    string? Status = null,
    bool ExceptionsOnly = false,
    int Page = 1,
    int PageSize = 50) : IRequest<HrAttendanceReportPage>;

/// <summary>A reduced punch record linked to one shift-start WorkDate attendance row.</summary>
public sealed record HrAttendanceReportPunch(
    DateTime PunchTimeLocal,
    DateTimeOffset? PunchTimeUtc,
    string? DirectionCode,
    string SourceType);

/// <summary>Holiday classification evidence retained by attendance processing.</summary>
public sealed record HrAttendanceReportHoliday(string Code, string Name, string HolidayType);

/// <summary>Effective weekly-off evidence retained by attendance processing.</summary>
public sealed record HrAttendanceReportWeeklyOff(int VersionNo, IReadOnlyList<DayOfWeek> Days);

/// <summary>A safe attendance report row with frozen work context and correction visibility.</summary>
public sealed record HrAttendanceReportRow(
    long Id,
    long EmployeeId,
    string EmployeeCode,
    string EmployeeName,
    DateOnly WorkDate,
    long LocationProjectionId,
    string LocationName,
    long ShiftId,
    string ShiftCode,
    string ShiftName,
    TimeOnly ShiftStartTimeLocal,
    TimeOnly ShiftEndTimeLocal,
    bool ShiftCrossesMidnight,
    string DayClassification,
    IReadOnlyList<HrAttendanceReportHoliday> HolidayEvidence,
    HrAttendanceReportWeeklyOff? WeeklyOffEvidence,
    DateTime? FirstInAtLocal,
    DateTime? LastOutAtLocal,
    int WorkedMinutes,
    int BreakMinutes,
    int LateMinutes,
    int EarlyLeaveMinutes,
    string Status,
    bool IsCorrected,
    int ProcessingVersion,
    DateTimeOffset LastProcessedAtUtc,
    int CorrectionCount,
    int? LatestCorrectionSequence,
    string? LatestCorrectionStatus,
    IReadOnlyList<HrAttendanceReportPunch> Punches);

/// <summary>Attendance report rows and aggregate exception counts for the applied filters.</summary>
public sealed record HrAttendanceReportPage(
    IReadOnlyList<HrAttendanceReportRow> Items,
    int Page,
    int PageSize,
    long TotalCount,
    long LateCount,
    long EarlyLeaveCount,
    long AbsentCount,
    long ExceptionCount);

public sealed record HrAttendanceReportRawPunch(
    long Id,
    DateTime PunchTimeLocal,
    DateTime? PunchTimeUtc,
    string? DirectionCode,
    string SourceType);

public sealed record HrAttendanceReportCorrection(
    long DailyAttendanceId,
    int Sequence,
    string Status);

public interface IHrAttendanceReportStore
{
    Task<HrAttendanceReportPage> QueryAsync(
        long businessId,
        HrAttendanceReportQuery request,
        HrOperationalLocationReadScope scope,
        CancellationToken cancellationToken);
}

public sealed class HrAttendanceReportHandler(
    IHrOrganisationAccess access,
    IHrAttendanceReportStore store) :
    IRequestHandler<HrAttendanceReportQuery, HrAttendanceReportPage>
{
    private static readonly HashSet<string> Statuses = new(StringComparer.Ordinal)
    {
        "Present", "Late", "HalfDay", "MissingPunch", "Absent", "Holiday", "WeeklyOff"
    };

    public async Task<HrAttendanceReportPage> Handle(HrAttendanceReportQuery request, CancellationToken ct)
    {
        if (request.From == default || request.To < request.From
            || request.To.DayNumber - request.From.DayNumber > 366
            || request.EmployeeId is <= 0
            || request.LocationProjectionId is <= 0
            || request.Page < 1 || request.PageSize is < 1 or > 200
            || (long)(request.Page - 1) * request.PageSize > int.MaxValue
            || (request.Status is not null && !Statuses.Contains(request.Status)))
            throw new ArgumentException("Attendance report filters or paging values are invalid.");

        var businessId = access.RequireBusinessId();
        await access.EnsureAsync(businessId, HrOrganisationOperation.ViewAttendanceReports,
            request.LocationProjectionId, ct);
        return await store.QueryAsync(businessId, request,
            await access.GetLocationReadScopeAsync(businessId, ct), ct);
    }
}
