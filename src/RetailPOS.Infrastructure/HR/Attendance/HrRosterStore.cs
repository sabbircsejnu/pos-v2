using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RetailPOS.Application.HR.Attendance;
using RetailPOS.Application.HR.Calendars;
using RetailPOS.Application.HR.Organisation;
using RetailPOS.Core.Entities.HR.Attendance;
using RetailPOS.Core.Entities.HR.Calendars;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.HR.Attendance;

public sealed class HrRosterStore(RetailPOSDbContext db, IHrCalendarStore calendars, TimeProvider clock) : IHrRosterStore
{
    private DateOnly Today => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
    public async Task<IReadOnlyList<HrRosterResponse>> ListAsync(long business, DateOnly from, DateOnly to,
        long? employeeId, HrOperationalLocationReadScope scope, CancellationToken ct)
    {
        if (scope.BusinessId != business) throw new UnauthorizedAccessException("Roster scope is outside Business.");
        var query = db.Set<HrRosterAssignment>().AsNoTracking().Where(r => r.BusinessId == business
            && r.WorkDate >= from && r.WorkDate <= to && (employeeId == null || r.EmployeeId == employeeId));
        if (scope.AllowedSources is not null)
        {
            var sourceSet = scope.AllowedSources.ToHashSet();
            var projections = await db.OperationalLocationProjections.AsNoTracking().Where(p => p.BusinessId == business)
                .Select(p => new { p.Id, p.SourceEntityType, p.SourceEntityId }).ToListAsync(ct);
            var ids = projections.Where(p => sourceSet.Contains(new(p.SourceEntityType, p.SourceEntityId))).Select(p => p.Id).ToArray();
            query = query.Where(r => Enumerable.Contains(ids, r.OperationalLocationProjectionId));
        }
        return (await query.OrderBy(r => r.WorkDate).ThenBy(r => r.EmployeeId).ThenBy(r => r.Id).ToListAsync(ct)).Select(Map).ToArray();
    }
    public async Task<HrRosterAccessContext> AccessContextAsync(long b, long? id, HrRosterData? data, CancellationToken ct)
    {
        var input = data ?? Data(await Find(b, id ?? throw new ArgumentException("Roster identity required."), ct));
        if (!await db.HrEmployees.AnyAsync(e => e.BusinessId == b && e.Id == input.EmployeeId, ct))
            throw new KeyNotFoundException("Employee not found.");
        var today = Today;
        var locations = await db.HrEmployeeLocationAssignments.AsNoTracking().Where(a => a.BusinessId == b
            && a.EmployeeId == input.EmployeeId && a.AssignmentType == "Primary"
            && ((a.EffectiveFrom <= input.WorkDate && (a.EffectiveTo == null || a.EffectiveTo >= input.WorkDate))
                || (a.Status == "Effective" && a.EffectiveFrom <= today && (a.EffectiveTo == null || a.EffectiveTo >= today))))
            .Select(a => a.OperationalLocationProjectionId).Distinct().ToArrayAsync(ct);
        return new(input.EmployeeId, input.OperationalLocationProjectionId, locations);
    }
    public async Task<HrRosterResponse> SaveAsync(long b, long? id, long? version, HrRosterData data, HrOperationalLocationReadScope scope, CancellationToken ct)
    {
        await Lock(b, ct);
        var row = id.HasValue ? await Find(b, id.Value, ct) : new HrRosterAssignment { BusinessId = b };
        if (id.HasValue) { await db.Entry(row).ReloadAsync(ct); Check(row, version); }
        if (row.Status != "Draft") throw new HrCalendarConflictException("Only draft rosters can be edited.");
        if (row.Id != 0 && row.WorkDate < Today || data.WorkDate < Today)
            throw new HrCalendarConflictException("Historical roster dates cannot be edited.");
        if (db.Database.IsNpgsql() && row.Id != 0 && row.EmployeeId != data.EmployeeId)
            foreach (var employee in new[] { row.EmployeeId, data.EmployeeId }.Order())
                await db.Database.SqlQuery<long>(
                    $"SELECT id AS \"Value\" FROM hr_employees WHERE business_id = {b} AND id = {employee} FOR UPDATE").ToListAsync(ct);
        await ValidateReferences(b, data, ct);
        await ValidateScope(b, data, scope, ct);
        if (id.HasValue) await ValidateScope(b, Data(row), scope, ct);
        await EnsureNoDuplicate(b, row.Id, data, ct);
        row.EmployeeId = data.EmployeeId; row.WorkDate = data.WorkDate; row.ShiftId = data.ShiftId;
        row.OperationalLocationProjectionId = data.OperationalLocationProjectionId;
        row.Source = data.Source; row.WorkKind = data.WorkKind; row.DayClassification = data.DayClassification;
        if (id.HasValue) row.Version++; else db.Add(row);
        await Save(ct); return Map(row);
    }
    public async Task<HrRosterResponse> TransitionAsync(long b, long id, long version, string status, long actor, HrOperationalLocationReadScope scope, CancellationToken ct)
    {
        await Lock(b, ct);
        var row = await Find(b, id, ct); await db.Entry(row).ReloadAsync(ct); Check(row, version);
        if (db.Database.IsNpgsql()) await db.Database.SqlQuery<long>(
            $"SELECT id AS \"Value\" FROM hr_employees WHERE business_id = {b} AND id = {row.EmployeeId} FOR UPDATE").ToListAsync(ct);
        await ValidateScope(b, Data(row), scope, ct);
        if (row.Status == "Cancelled" || status == "Published" && row.Status != "Draft")
            throw new HrCalendarConflictException("Roster transition is not valid.");
        if (row.WorkDate < Today || row.Status == "Published" && row.WorkDate <= Today)
            throw new HrCalendarConflictException("Historical/current published rosters cannot be replaced or cancelled.");
        if (status == "Published")
        {
            await ValidateReferences(b, Data(row), ct);
            await EnsureNoDuplicate(b, id, Data(row), ct);
            var shift = (await calendars.ShiftsAsync(b, null, ct)).Single(s => s.Id == row.ShiftId);
            row.ShiftSnapshotJson = JsonSerializer.Serialize(shift.Data);
            var location = await db.OperationalLocationProjections.AsNoTracking()
                .SingleAsync(l => l.BusinessId == b && l.Id == row.OperationalLocationProjectionId, ct);
            row.LocationSnapshotJson = JsonSerializer.Serialize(new HrRosterLocationSnapshot(location.Id, location.Name,
                location.SourceSystem, location.SourceEntityType, location.SourceEntityId, location.SourceVersion));
            var evidence = await calendars.ClassifyAsync(b, row.WorkDate, location.Id, ct);
            var classification = HrDayClassification.Resolve(row.WorkDate, evidence.Holidays, evidence.WeeklyOffPolicy,
                publishedRoster: new(row.DayClassification, row.DayClassification == "WeeklyOff"));
            row.CalendarEvidenceJson = JsonSerializer.Serialize(classification);
            row.PublishedByUserId = actor; row.PublishedAtUtc = clock.GetUtcNow().UtcDateTime;
            await EnsureWindowDoesNotOverlap(b, row, shift.Data, ct);
        }
        row.Status = status; row.Version++; await Save(ct); return Map(row);
    }
    public async Task<HrRosterResponse?> PublishedAsync(long b, long employeeId, DateOnly date, CancellationToken ct)
    {
        if (employeeId <= 0 || date == default) throw new ArgumentException("Employee and WorkDate required.");
        var row = await db.Set<HrRosterAssignment>().AsNoTracking().SingleOrDefaultAsync(r => r.BusinessId == b
            && r.EmployeeId == employeeId && r.WorkDate == date && r.Status == "Published", ct);
        return row is null ? null : Map(row);
    }
    private async Task ValidateReferences(long b, HrRosterData data, CancellationToken ct)
    {
        HrRosterRules.Validate(data.WorkDate, data.EmployeeId, data.ShiftId, data.OperationalLocationProjectionId,
            data.Source, data.WorkKind, data.DayClassification);
        if (db.Database.IsNpgsql())
            await db.Database.SqlQuery<long>($"SELECT id AS \"Value\" FROM hr_employees WHERE business_id = {b} AND id = {data.EmployeeId} FOR UPDATE").ToListAsync(ct);
        if (!await db.HrEmploymentPeriods.AnyAsync(p => p.BusinessId == b && p.EmployeeId == data.EmployeeId
            && p.StartDate <= data.WorkDate && (p.EndDate == null || p.EndDate >= data.WorkDate), ct))
            throw new ArgumentException("Employee must have an effective employment period for WorkDate.");
        if (!await db.HrEmployees.AnyAsync(e => e.BusinessId == b && e.Id == data.EmployeeId
            && e.CurrentEmploymentStatus == "Active", ct))
            throw new ArgumentException("Employee must be active to receive a new roster.");
        var shift = await db.Set<HrShift>().AsNoTracking().Include(s => s.Breaks)
            .SingleOrDefaultAsync(s => s.BusinessId == b && s.Id == data.ShiftId, ct)
            ?? throw new ArgumentException("Same-Business shift required.");
        HrShiftRules.EnsureAssignable(shift);
        await Organisation.HrOperationalLocationReference.RequireOwnedAsync(db, b, data.OperationalLocationProjectionId, ct);
        if (!await db.HrEmployeeLocationAssignments.AnyAsync(a => a.BusinessId == b && a.EmployeeId == data.EmployeeId
            && a.OperationalLocationProjectionId == data.OperationalLocationProjectionId
            && a.EffectiveFrom <= data.WorkDate && (a.EffectiveTo == null || a.EffectiveTo >= data.WorkDate)
            && a.Status != "Cancelled", ct))
            throw new ArgumentException("Employee must have an effective assignment at the roster location.");
    }
    private async Task ValidateScope(long b, HrRosterData data, HrOperationalLocationReadScope scope, CancellationToken ct)
    {
        await Organisation.HrOperationalLocationReference.EnsureAuthorizedAsync(db, b, data.OperationalLocationProjectionId, scope, ct);
        if (scope.AllowedSources is null) return;
        var context = await AccessContextAsync(b, null, data, ct);
        if (context.EmployeePrimaryLocations.Count == 0)
            throw new UnauthorizedAccessException("Employee has no authorized Primary location context.");
        foreach (var location in context.EmployeePrimaryLocations)
            await Organisation.HrOperationalLocationReference.EnsureAuthorizedAsync(db, b, location, scope, ct);
    }
    private async Task EnsureWindowDoesNotOverlap(long b, HrRosterAssignment row, HrShiftData shift, CancellationToken ct)
    {
        var before = row.WorkDate.AddDays(-1); var after = row.WorkDate.AddDays(1);
        var others = await db.Set<HrRosterAssignment>().AsNoTracking().Where(r => r.BusinessId == b
            && r.EmployeeId == row.EmployeeId && r.Status == "Published" && r.Id != row.Id
            && r.WorkDate >= before && r.WorkDate <= after).ToListAsync(ct);
        var window = HrShiftRules.Window(row.WorkDate, shift.StartTimeLocal, shift.EndTimeLocal, shift.CrossesMidnight);
        foreach (var other in others)
        {
            var s = Deserialize<HrShiftData>(other.ShiftSnapshotJson)!;
            var w = HrShiftRules.Window(other.WorkDate, s.StartTimeLocal, s.EndTimeLocal, s.CrossesMidnight);
            if (window.StartLocal.AddMinutes(-shift.AllowedEarlyPunchMinutes) <= w.EndLocal.AddMinutes(s.AllowedLatePunchMinutes)
                && w.StartLocal.AddMinutes(-s.AllowedEarlyPunchMinutes) <= window.EndLocal.AddMinutes(shift.AllowedLatePunchMinutes))
                throw new HrCalendarConflictException("Published roster punch windows overlap an adjacent WorkDate.");
            var rest = w.EndLocal <= window.StartLocal ? (window.StartLocal - w.EndLocal).TotalMinutes
                : window.EndLocal <= w.StartLocal ? (w.StartLocal - window.EndLocal).TotalMinutes : -1;
            var required = w.EndLocal <= window.StartLocal ? s.MinimumRestBeforeNextShiftMinutes : shift.MinimumRestBeforeNextShiftMinutes;
            if (required.HasValue && rest < required.Value)
                throw new HrCalendarConflictException("Adjacent rosters violate configured minimum rest.");
        }
    }
    private async Task EnsureNoDuplicate(long b, long id, HrRosterData data, CancellationToken ct)
    {
        if (await db.Set<HrRosterAssignment>().AnyAsync(r => r.BusinessId == b && r.EmployeeId == data.EmployeeId
            && r.WorkDate == data.WorkDate && r.Status != "Cancelled" && r.Id != id, ct))
            throw new HrCalendarConflictException("An active roster already exists for this employee/WorkDate.");
    }
    private async Task Lock(long b, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Roster writes require an audited transaction.");
        if (db.Database.IsNpgsql())
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({b ^ (long.MinValue + 2)})", ct);
    }
    private async Task<HrRosterAssignment> Find(long b, long id, CancellationToken ct) =>
        await db.Set<HrRosterAssignment>().SingleOrDefaultAsync(r => r.BusinessId == b && r.Id == id, ct)
            ?? throw new KeyNotFoundException("Roster not found.");
    private static void Check(HrRosterAssignment row, long? expected) {
        if (row.Version != expected) throw new HrCalendarConflictException("Roster changed since it was read."); }
    private async Task Save(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException e) { throw new HrCalendarConflictException("Roster changed concurrently.", e); }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { throw new HrCalendarConflictException("An active roster already exists for this employee/WorkDate.", e); }
    }
    private static HrRosterData Data(HrRosterAssignment r) => new(r.EmployeeId, r.WorkDate, r.ShiftId,
        r.OperationalLocationProjectionId, r.Source, r.WorkKind, r.DayClassification);
    private static T? Deserialize<T>(string? json) where T : class =>
        json is null ? null : JsonSerializer.Deserialize<T>(json) ?? throw new InvalidOperationException("Invalid published roster context.");
    private static HrRosterResponse Map(HrRosterAssignment r) => new(r.Id, r.BusinessId, Data(r), r.Status, r.Version,
        r.PublishedAtUtc.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(
            r.PublishedAtUtc.Value.Kind == DateTimeKind.Local ? r.PublishedAtUtc.Value.ToUniversalTime() : r.PublishedAtUtc.Value, DateTimeKind.Utc)) : null,
        Deserialize<HrShiftData>(r.ShiftSnapshotJson), Deserialize<HrRosterLocationSnapshot>(r.LocationSnapshotJson),
        Deserialize<HrDayClassificationResponse>(r.CalendarEvidenceJson));
}
