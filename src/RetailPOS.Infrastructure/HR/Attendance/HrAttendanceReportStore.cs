using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.HR.Attendance;
using RetailPOS.Application.HR.Calendars;
using RetailPOS.Application.HR.Organisation;
using RetailPOS.Core.Entities.HR.Attendance;
using RetailPOS.Core.Entities.HR.Employees;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.HR.Attendance;

public sealed class HrAttendanceReportStore(RetailPOSDbContext db) : IHrAttendanceReportStore
{
    public async Task<HrAttendanceReportPage> QueryAsync(
        long businessId,
        HrAttendanceReportQuery request,
        HrOperationalLocationReadScope scope,
        CancellationToken cancellationToken)
    {
        if (scope.BusinessId != businessId)
            throw new UnauthorizedAccessException("Attendance report scope is outside Business.");

        var query = db.Set<HrDailyAttendance>().AsNoTracking()
            .Where(row => row.BusinessId == businessId
                && row.WorkDate >= request.From && row.WorkDate <= request.To
                && (request.EmployeeId == null || row.EmployeeId == request.EmployeeId)
                && (request.LocationProjectionId == null
                    || row.OperationalLocationProjectionId == request.LocationProjectionId));

        if (request.Status is not null)
            query = query.Where(row => row.Status == request.Status);
        if (request.ExceptionsOnly)
            query = query.Where(row => row.LateMinutes > 0 || row.EarlyLeaveMinutes > 0
                || row.Status == "Absent" || row.Status == "MissingPunch" || row.Status == "HalfDay");

        if (scope.AllowedSources is not null)
        {
            var allowedSources = scope.AllowedSources.ToHashSet();
            var locationIds = (await db.OperationalLocationProjections.AsNoTracking()
                    .Where(location => location.BusinessId == businessId)
                    .Select(location => new
                    {
                        location.Id,
                        location.SourceEntityType,
                        location.SourceEntityId
                    }).ToListAsync(cancellationToken))
                .Where(location => allowedSources.Contains(new OperationalLocationSourceIdentity(
                    location.SourceEntityType, location.SourceEntityId)))
                .Select(location => location.Id)
                .ToArray();
            query = query.Where(row => Enumerable.Contains(locationIds, row.OperationalLocationProjectionId));
        }

        var aggregate = await query.GroupBy(_ => 1).Select(group => new
        {
            TotalCount = group.LongCount(),
            LateCount = group.LongCount(row => row.LateMinutes > 0),
            EarlyLeaveCount = group.LongCount(row => row.EarlyLeaveMinutes > 0),
            AbsentCount = group.LongCount(row => row.Status == "Absent"),
            ExceptionCount = group.LongCount(row => row.LateMinutes > 0 || row.EarlyLeaveMinutes > 0
                || row.Status == "Absent" || row.Status == "MissingPunch" || row.Status == "HalfDay")
        }).SingleOrDefaultAsync(cancellationToken);

        var offset = (request.Page - 1) * request.PageSize;
        var pageData = await (from row in query
                              join employee in db.Set<HrEmployee>().AsNoTracking()
                                  on new { row.BusinessId, row.EmployeeId }
                                  equals new { employee.BusinessId, EmployeeId = employee.Id }
                              orderby row.WorkDate descending, row.EmployeeId
                              select new
                              {
                                  Attendance = row,
                                  employee.EmployeeCode,
                                  employee.DisplayName
                              })
            .Skip(offset)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var dailyRows = pageData.Select(item => item.Attendance).ToArray();
        var punchIds = dailyRows.SelectMany(row => JsonSerializer.Deserialize<long[]>(row.PunchIdsJson)
                ?? throw new InvalidOperationException($"Attendance row {row.Id} has invalid punch evidence."))
            .Distinct()
            .ToArray();
        var punches = punchIds.Length == 0
            ? []
            : await db.Set<HrRawAttendancePunch>().AsNoTracking()
                .Where(punch => punch.BusinessId == businessId
                    && Enumerable.Contains(punchIds, punch.Id))
                .Select(punch => new HrAttendanceReportRawPunch(
                    punch.Id, punch.PunchTimeLocal, punch.PunchTimeUtc, punch.DirectionCode, punch.SourceType))
                .ToListAsync(cancellationToken);
        var missingPunchIds = punchIds.Except(punches.Select(punch => punch.Id)).ToArray();
        if (missingPunchIds.Length > 0)
            throw new InvalidOperationException(
                $"Attendance report references missing raw punch evidence: {string.Join(",", missingPunchIds)}.");
        var punchLookup = punches.ToDictionary(punch => punch.Id);

        var dailyIds = dailyRows.Select(row => row.Id).ToArray();
        var corrections = dailyIds.Length == 0
            ? []
            : await db.Set<HrAttendanceCorrectionRequest>().AsNoTracking()
                .Where(correction => correction.BusinessId == businessId
                    && Enumerable.Contains(dailyIds, correction.DailyAttendanceId))
                .OrderBy(correction => correction.CorrectionSequence)
                .Select(correction => new HrAttendanceReportCorrection(
                    correction.DailyAttendanceId, correction.CorrectionSequence, correction.Status))
                .ToListAsync(cancellationToken);
        var correctionLookup = corrections.GroupBy(correction => correction.DailyAttendanceId)
            .ToDictionary(group => group.Key, group => group.ToArray());

        var rows = pageData.Select(item => Map(
            item.Attendance, item.EmployeeCode, item.DisplayName,
            correctionLookup.GetValueOrDefault(item.Attendance.Id) ?? [],
            punchLookup)).ToArray();

        return new HrAttendanceReportPage(rows, request.Page, request.PageSize,
            aggregate?.TotalCount ?? 0, aggregate?.LateCount ?? 0, aggregate?.EarlyLeaveCount ?? 0,
            aggregate?.AbsentCount ?? 0, aggregate?.ExceptionCount ?? 0);
    }

    private static HrAttendanceReportRow Map(
        HrDailyAttendance row,
        string employeeCode,
        string employeeName,
        IReadOnlyList<HrAttendanceReportCorrection> corrections,
        IReadOnlyDictionary<long, HrAttendanceReportRawPunch> punches)
    {
        var shift = JsonSerializer.Deserialize<HrShiftData>(row.ShiftSnapshotJson)
            ?? throw new InvalidOperationException($"Attendance row {row.Id} has invalid shift evidence.");
        var location = JsonSerializer.Deserialize<HrRosterLocationSnapshot>(row.LocationSnapshotJson)
            ?? throw new InvalidOperationException($"Attendance row {row.Id} has invalid location evidence.");
        var calendar = JsonSerializer.Deserialize<HrDayClassificationResponse>(row.CalendarEvidenceJson)
            ?? throw new InvalidOperationException($"Attendance row {row.Id} has invalid calendar evidence.");
        var ids = JsonSerializer.Deserialize<long[]>(row.PunchIdsJson)
            ?? throw new InvalidOperationException($"Attendance row {row.Id} has invalid punch evidence.");
        var latestCorrection = corrections.LastOrDefault();

        return new HrAttendanceReportRow(
            row.Id, row.EmployeeId, employeeCode, employeeName, row.WorkDate,
            row.OperationalLocationProjectionId, location.Name, row.ShiftId,
            shift.Code, shift.Name, shift.StartTimeLocal, shift.EndTimeLocal, shift.CrossesMidnight,
            row.DayClassification,
            calendar.Holidays.Select(holiday => new HrAttendanceReportHoliday(
                holiday.Data.Code, holiday.Data.Name, holiday.Data.HolidayType)).ToArray(),
            calendar.WeeklyOffPolicy is { } weeklyOff
                ? new HrAttendanceReportWeeklyOff(weeklyOff.VersionNo, weeklyOff.Data.Days)
                : null,
            row.FirstInAtLocal, row.LastOutAtLocal, row.WorkedMinutes, row.BreakMinutes,
            row.LateMinutes, row.EarlyLeaveMinutes, row.Status,
            row.IsManuallyAdjusted || corrections.Any(correction =>
                correction.Status is "Approved" or "Applied"),
            row.ProcessingVersion,
            new DateTimeOffset(DateTime.SpecifyKind(row.LastProcessedAtUtc, DateTimeKind.Utc)),
            corrections.Count, latestCorrection?.Sequence, latestCorrection?.Status,
            ids.Select(id => punches[id]).OrderBy(punch => punch.PunchTimeLocal)
                .Select(punch => new HrAttendanceReportPunch(
                    punch.PunchTimeLocal,
                    punch.PunchTimeUtc is { } utc
                        ? new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc))
                        : null,
                    punch.DirectionCode,
                    punch.SourceType))
                .ToArray());
    }
}
