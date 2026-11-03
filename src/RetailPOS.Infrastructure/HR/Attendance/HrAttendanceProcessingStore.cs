using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.HR.Attendance;
using RetailPOS.Application.HR.Calendars;
using RetailPOS.Application.HR.Organisation;
using RetailPOS.Core.Entities.HR.Attendance;
using RetailPOS.Core.Entities.HR.Calendars;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.HR.Attendance;

public sealed class HrAttendanceProcessingStore(RetailPOSDbContext db, TimeProvider clock) : IHrAttendanceProcessingStore
{
    public async Task<IReadOnlyList<HrDailyAttendanceResponse>> ProcessAsync(long businessId, DateOnly from,
        DateOnly to, long? employeeId, HrOperationalLocationReadScope scope, CancellationToken ct)
    {
        await HrAttendanceBusinessLock.LockAsync(db, businessId, ct);
        var rosters = await PublishedRosters(businessId, from, to, employeeId, scope, ct);
        var results = new List<HrDailyAttendance>(rosters.Count);
        foreach (var roster in rosters)
        {
            if (string.IsNullOrWhiteSpace(roster.ShiftSnapshotJson)
                || string.IsNullOrWhiteSpace(roster.LocationSnapshotJson)
                || string.IsNullOrWhiteSpace(roster.CalendarEvidenceJson))
                throw new InvalidOperationException($"Published roster {roster.Id} has incomplete frozen context.");

            var shift = JsonSerializer.Deserialize<HrShiftData>(roster.ShiftSnapshotJson)
                ?? throw new InvalidOperationException($"Published roster {roster.Id} has an invalid shift snapshot.");
            var calendar = JsonSerializer.Deserialize<HrDayClassificationResponse>(roster.CalendarEvidenceJson)
                ?? throw new InvalidOperationException($"Published roster {roster.Id} has invalid calendar evidence.");
            var location = JsonSerializer.Deserialize<HrRosterLocationSnapshot>(roster.LocationSnapshotJson)
                ?? throw new InvalidOperationException($"Published roster {roster.Id} has an invalid location snapshot.");
            var window = HrShiftRules.Window(roster.WorkDate, shift.StartTimeLocal, shift.EndTimeLocal, shift.CrossesMidnight);
            var start = window.StartLocal.AddMinutes(-shift.AllowedEarlyPunchMinutes);
            var end = window.EndLocal.AddMinutes(shift.AllowedLatePunchMinutes);
            var rawPunches = await db.Set<HrRawAttendancePunch>().Where(p => p.BusinessId == businessId
                    && p.EmployeeId == roster.EmployeeId && p.PunchTimeUtc != null
                    && p.PunchTimeLocal >= start && p.PunchTimeLocal <= end)
                .OrderBy(p => p.PunchTimeLocal).ThenBy(p => p.Id).ToListAsync(ct);
            var calculated = HrAttendanceProcessingRules.Calculate(
                new HrAttendanceSchedule(roster.WorkDate, shift, calendar.Classification),
                rawPunches.Select(p => new HrAttendancePunchSample(p.Id, p.PunchTimeLocal,
                    p.PunchTimeUtc.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(p.PunchTimeUtc.Value, DateTimeKind.Utc)) : null,
                    p.TimeZoneId, p.DirectionCode)).ToArray());
            var fingerprint = Fingerprint(roster, shift, location, calendar, calculated);
            var daily = await db.Set<HrDailyAttendance>().SingleOrDefaultAsync(a => a.BusinessId == businessId
                && a.EmployeeId == roster.EmployeeId && a.WorkDate == roster.WorkDate, ct);
            if (daily is null)
            {
                daily = new HrDailyAttendance
                {
                    BusinessId = businessId,
                    EmployeeId = roster.EmployeeId,
                    WorkDate = roster.WorkDate,
                    OperationalLocationProjectionId = roster.OperationalLocationProjectionId,
                    SourceLocationTypeSnapshot = location.SourceEntityType,
                    ShiftId = roster.ShiftId,
                    RosterAssignmentId = roster.Id,
                    DayClassification = calendar.Classification,
                    TimeZoneIdSnapshot = calculated.TimeZoneIdSnapshot,
                    FirstInAtLocal = calculated.FirstInAtLocal,
                    LastOutAtLocal = calculated.LastOutAtLocal,
                    WorkedMinutes = calculated.WorkedMinutes,
                    BreakMinutes = calculated.BreakMinutes,
                    LateMinutes = calculated.LateMinutes,
                    EarlyLeaveMinutes = calculated.EarlyLeaveMinutes,
                    OvertimeMinutesCalculated = 0,
                    Status = calculated.Status,
                    ProcessingVersion = 1,
                    LastProcessedAtUtc = clock.GetUtcNow().UtcDateTime,
                    ShiftSnapshotJson = roster.ShiftSnapshotJson,
                    LocationSnapshotJson = roster.LocationSnapshotJson,
                    CalendarEvidenceJson = roster.CalendarEvidenceJson,
                    PunchIdsJson = JsonSerializer.Serialize(calculated.PunchIds),
                    CalculationFingerprint = fingerprint
                };
                db.Add(daily);
            }
            else if (!string.Equals(daily.CalculationFingerprint, fingerprint, StringComparison.Ordinal))
            {
                if (await db.Set<HrAttendanceCorrectionRequest>().AnyAsync(row =>
                        row.BusinessId == businessId && row.DailyAttendanceId == daily.Id
                        && row.Status == "PendingApproval", ct))
                    throw new InvalidOperationException(
                        "Attendance has a pending correction; decide or cancel it before reprocessing.");
                if (daily.ProcessingVersion == int.MaxValue)
                    throw new InvalidOperationException("Attendance processing version is exhausted.");
                if (!daily.IsManuallyAdjusted)
                {
                    daily.OperationalLocationProjectionId = roster.OperationalLocationProjectionId;
                    daily.SourceLocationTypeSnapshot = location.SourceEntityType;
                    daily.ShiftId = roster.ShiftId;
                    daily.RosterAssignmentId = roster.Id;
                    daily.DayClassification = calendar.Classification;
                    daily.TimeZoneIdSnapshot = calculated.TimeZoneIdSnapshot;
                    daily.FirstInAtLocal = calculated.FirstInAtLocal;
                    daily.LastOutAtLocal = calculated.LastOutAtLocal;
                    daily.WorkedMinutes = calculated.WorkedMinutes;
                    daily.BreakMinutes = calculated.BreakMinutes;
                    daily.LateMinutes = calculated.LateMinutes;
                    daily.EarlyLeaveMinutes = calculated.EarlyLeaveMinutes;
                    daily.OvertimeMinutesCalculated = 0;
                    daily.Status = calculated.Status;
                    daily.ShiftSnapshotJson = roster.ShiftSnapshotJson;
                    daily.LocationSnapshotJson = roster.LocationSnapshotJson;
                    daily.CalendarEvidenceJson = roster.CalendarEvidenceJson;
                }
                daily.ProcessingVersion++;
                daily.LastProcessedAtUtc = clock.GetUtcNow().UtcDateTime;
                daily.PunchIdsJson = JsonSerializer.Serialize(calculated.PunchIds);
                daily.CalculationFingerprint = fingerprint;
            }
            foreach (var punch in rawPunches.Where(p => calculated.PunchIds.Contains(p.Id)))
            {
                if (punch.ProcessingStatus != "Processed" || punch.ProcessingError is not null)
                {
                    punch.ProcessingStatus = "Processed";
                    punch.ProcessingError = null;
                    punch.ProcessingVersion++;
                }
            }
            results.Add(daily);
        }
        await db.SaveChangesAsync(ct);
        return results.Select(Map).ToArray();
    }

    public async Task<IReadOnlyList<HrDailyAttendanceResponse>> ListAsync(long businessId, DateOnly from,
        DateOnly to, long? employeeId, HrOperationalLocationReadScope scope, CancellationToken ct)
    {
        if (scope.BusinessId != businessId) throw new UnauthorizedAccessException("Attendance scope is outside Business.");
        var query = db.Set<HrDailyAttendance>().AsNoTracking().Where(a => a.BusinessId == businessId
            && a.WorkDate >= from && a.WorkDate <= to && (employeeId == null || a.EmployeeId == employeeId));
        if (scope.AllowedSources is not null)
        {
            var allowed = scope.AllowedSources.ToHashSet();
            var projections = await db.OperationalLocationProjections.AsNoTracking().Where(p => p.BusinessId == businessId)
                .Select(p => new { p.Id, p.SourceEntityType, p.SourceEntityId }).ToListAsync(ct);
            var ids = projections.Where(p => allowed.Contains(new(p.SourceEntityType, p.SourceEntityId)))
                .Select(p => p.Id).ToArray();
            query = query.Where(a => Enumerable.Contains(ids, a.OperationalLocationProjectionId));
        }
        return (await query.OrderBy(a => a.WorkDate).ThenBy(a => a.EmployeeId).ToListAsync(ct)).Select(Map).ToArray();
    }

    private async Task<List<HrRosterAssignment>> PublishedRosters(long businessId, DateOnly from, DateOnly to,
        long? employeeId, HrOperationalLocationReadScope scope, CancellationToken ct)
    {
        if (scope.BusinessId != businessId) throw new UnauthorizedAccessException("Attendance scope is outside Business.");
        var query = db.Set<HrRosterAssignment>().Where(r => r.BusinessId == businessId && r.Status == "Published"
            && r.WorkDate >= from && r.WorkDate <= to && (employeeId == null || r.EmployeeId == employeeId));
        if (scope.AllowedSources is not null)
        {
            var allowed = scope.AllowedSources.ToHashSet();
            var projections = await db.OperationalLocationProjections.AsNoTracking().Where(p => p.BusinessId == businessId)
                .Select(p => new { p.Id, p.SourceEntityType, p.SourceEntityId }).ToListAsync(ct);
            var ids = projections.Where(p => allowed.Contains(new(p.SourceEntityType, p.SourceEntityId)))
                .Select(p => p.Id).ToArray();
            query = query.Where(r => Enumerable.Contains(ids, r.OperationalLocationProjectionId));
        }
        return await query.OrderBy(r => r.WorkDate).ThenBy(r => r.EmployeeId).ToListAsync(ct);
    }

    private static string Fingerprint(HrRosterAssignment roster, HrShiftData shift,
        HrRosterLocationSnapshot location, HrDayClassificationResponse calendar, HrAttendanceCalculation result)
    {
        var evidence = JsonSerializer.Serialize(new
        {
            roster.Id, roster.EmployeeId, roster.WorkDate, roster.OperationalLocationProjectionId,
            Shift = shift, Location = location, Calendar = calendar,
            result.FirstInAtLocal, result.LastOutAtLocal, result.WorkedMinutes, result.BreakMinutes,
            result.LateMinutes, result.EarlyLeaveMinutes, result.Status, result.PunchIds, result.TimeZoneIdSnapshot
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(evidence)));
    }

    private static HrDailyAttendanceResponse Map(HrDailyAttendance a) => new(a.Id, a.BusinessId, a.EmployeeId,
        a.WorkDate, a.OperationalLocationProjectionId, a.ShiftId, a.RosterAssignmentId, a.DayClassification,
        a.TimeZoneIdSnapshot, a.FirstInAtLocal, a.LastOutAtLocal, a.WorkedMinutes, a.BreakMinutes,
        a.LateMinutes, a.EarlyLeaveMinutes, a.OvertimeMinutesCalculated, a.Status, a.ProcessingVersion,
        new DateTimeOffset(DateTime.SpecifyKind(a.LastProcessedAtUtc, DateTimeKind.Utc)), a.IsManuallyAdjusted,
        JsonSerializer.Deserialize<long[]>(a.PunchIdsJson) ?? []);
}
