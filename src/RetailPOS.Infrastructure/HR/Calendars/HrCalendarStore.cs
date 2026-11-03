using Microsoft.EntityFrameworkCore;
using Npgsql;
using RetailPOS.Application.HR.Calendars;
using RetailPOS.Application.HR.Organisation;
using RetailPOS.Core.Entities.HR.Calendars;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.HR.Calendars;

public sealed class HrCalendarStore(RetailPOSDbContext db, TimeProvider clock) : IHrCalendarStore
{
    private DateOnly Today => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    public async Task<IReadOnlyList<HrShiftResponse>> ShiftsAsync(long businessId, string? search, CancellationToken ct)
    {
        var query = db.Set<HrShift>().AsNoTracking().Include(r => r.Breaks).Where(r => r.BusinessId == businessId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(r => r.Code.ToLower().Contains(term) || r.Name.ToLower().Contains(term));
        }
        return (await query.OrderBy(r => r.Code).ToListAsync(ct)).Select(Map).ToArray();
    }

    public async Task<HrShiftResponse> SaveShiftAsync(long businessId, long? id, long? expectedVersion, HrShiftData? data, CancellationToken ct)
    {
        await Lock(businessId, ct);
        var row = id.HasValue
            ? await db.Set<HrShift>().Include(r => r.Breaks).SingleOrDefaultAsync(r => r.BusinessId == businessId && r.Id == id, ct)
                ?? throw new KeyNotFoundException("Shift not found.")
            : new HrShift { BusinessId = businessId };
        if (id.HasValue) { await db.Entry(row).ReloadAsync(ct); Check(row.Version, expectedVersion); }
        if (!row.IsActive) throw new HrCalendarConflictException("Inactive shifts cannot be changed or newly assigned.");
        if (data is null) row.IsActive = false;
        else
        {
            var code = data.Code.Trim().ToUpperInvariant();
            if (await db.Set<HrShift>().AnyAsync(r => r.BusinessId == businessId && r.Code == code && r.Id != row.Id, ct))
                throw new HrCalendarConflictException("Shift code already exists in this Business.");
            row.Code = code; row.Name = data.Name.Trim();
            row.StartTimeLocal = data.StartTimeLocal; row.EndTimeLocal = data.EndTimeLocal;
            row.CrossesMidnight = data.CrossesMidnight; row.MinimumWorkMinutes = data.MinimumWorkMinutes;
            row.MaximumAllowedWorkMinutes = data.MaximumAllowedWorkMinutes;
            row.AllowedEarlyPunchMinutes = data.AllowedEarlyPunchMinutes; row.AllowedLatePunchMinutes = data.AllowedLatePunchMinutes;
            row.MinimumRestBeforeNextShiftMinutes = data.MinimumRestBeforeNextShiftMinutes;
            row.GraceInMinutes = data.GraceInMinutes; row.GraceOutMinutes = data.GraceOutMinutes;
            row.OvertimeEligible = data.OvertimeEligible;
            // Delete old sequences before inserting replacements to respect the unique sequence constraint.
            if (id.HasValue)
            {
                db.RemoveRange(row.Breaks); row.Breaks.Clear();
                await Save(ct);
            }
            row.Breaks = data.Breaks.Select(b => new HrShiftBreak
                { Sequence = b.Sequence, DurationMinutes = b.DurationMinutes, IsPaid = b.IsPaid }).ToList();
            HrShiftRules.Validate(row);
        }
        if (id.HasValue) row.Version = checked(row.Version + 1); else db.Add(row);
        await Save(ct);
        return Map(row);
    }

    public async Task<IReadOnlyList<HrHolidayCalendarResponse>> CalendarsAsync(long businessId, HrOperationalLocationReadScope scope, CancellationToken ct)
    {
        var ids = await AllowedLocations(businessId, scope, ct);
        var rows = await db.Set<HrHolidayCalendar>().AsNoTracking().Where(r => r.BusinessId == businessId
            && (ids == null || r.OperationalLocationProjectionId == null || Enumerable.Contains(ids, r.OperationalLocationProjectionId.Value)))
            .OrderBy(r => r.Name).ToListAsync(ct);
        return rows.Select(Map).ToArray();
    }

    public async Task<HrHolidayCalendarResponse> SaveCalendarAsync(long businessId, long? id, long? expectedVersion,
        HrHolidayCalendarData? data, DateOnly? effectiveTo, CancellationToken ct)
    {
        await Lock(businessId, ct);
        var row = id.HasValue ? await Calendar(businessId, id.Value, ct)
            : new HrHolidayCalendar { BusinessId = businessId };
        if (id.HasValue) { await db.Entry(row).ReloadAsync(ct); Check(row.Version, expectedVersion); }
        if (!row.IsActive) throw new HrCalendarConflictException("Calendar is closed.");
        if (data is null)
        {
            End(row.EffectiveFrom, row.EffectiveTo, effectiveTo);
            if (await db.Set<HrHoliday>().AnyAsync(h => h.CalendarId == row.Id && h.IsActive && h.HolidayDate > effectiveTo, ct))
                throw new HrCalendarConflictException("Calendar end would exclude an active holiday.");
            row.EffectiveTo = effectiveTo; row.IsActive = false;
        }
        else
        {
            if (id.HasValue && row.EffectiveFrom <= Today)
                throw new HrCalendarConflictException("An effective calendar cannot be rewritten; create a future calendar.");
            if (id.HasValue && Scope(row) != data.Scope)
                throw new HrCalendarConflictException("Calendar scope is immutable; create a new scoped calendar.");
            await ValidateLocation(businessId, data.Scope, ct);
            if (id.HasValue && await db.Set<HrHoliday>().AnyAsync(h => h.CalendarId == row.Id
                && (h.HolidayDate < data.EffectiveFrom || data.EffectiveTo != null && h.HolidayDate > data.EffectiveTo), ct))
                throw new HrCalendarConflictException("Calendar period must contain its retained holidays.");
            row.Name = data.Name.Trim(); row.ScopeType = data.Scope.ScopeType;
            row.LocationType = data.Scope.LocationType; row.OperationalLocationProjectionId = data.Scope.OperationalLocationProjectionId;
            row.EffectiveFrom = data.EffectiveFrom; row.EffectiveTo = data.EffectiveTo;
        }
        if (id.HasValue) row.Version++; else db.Add(row);
        await Save(ct); return Map(row);
    }

    public async Task<IReadOnlyList<HrHolidayResponse>> HolidaysAsync(long businessId, long calendarId, CancellationToken ct) =>
        (await db.Set<HrHoliday>().AsNoTracking().Where(r => r.BusinessId == businessId && r.CalendarId == calendarId)
            .OrderBy(r => r.HolidayDate).ThenBy(r => r.Id).ToListAsync(ct)).Select(Map).ToArray();

    public async Task<HrHolidayResponse> SaveHolidayAsync(long businessId, long calendarId, long? id, long? expectedVersion,
        HrHolidayData? data, CancellationToken ct)
    {
        await Lock(businessId, ct);
        var calendar = await Calendar(businessId, calendarId, ct);
        await db.Entry(calendar).ReloadAsync(ct);
        if (!calendar.IsActive) throw new HrCalendarConflictException("Calendar is closed.");
        await ValidateLocation(businessId, Scope(calendar), ct);
        var row = id.HasValue ? await db.Set<HrHoliday>().SingleOrDefaultAsync(r => r.BusinessId == businessId
            && r.CalendarId == calendarId && r.Id == id, ct) ?? throw new KeyNotFoundException("Holiday not found.")
            : new HrHoliday { BusinessId = businessId, CalendarId = calendarId };
        if (id.HasValue)
        {
            await db.Entry(row).ReloadAsync(ct); Check(row.Version, expectedVersion);
            if (row.HolidayDate <= Today) throw new HrCalendarConflictException("Historical holidays cannot be rewritten.");
        }
        if (!row.IsActive) throw new HrCalendarConflictException("Holiday is inactive.");
        if (data is null) row.IsActive = false;
        else
        {
            if (data.HolidayDate <= Today || data.HolidayDate < calendar.EffectiveFrom
                || data.HolidayDate > calendar.EffectiveTo)
                throw new ArgumentException("Holiday must be future-dated and inside its calendar period.");
            var code = data.Code.Trim().ToUpperInvariant();
            if (await db.Set<HrHoliday>().AnyAsync(h => h.CalendarId == calendarId && h.Code == code && h.Id != row.Id, ct))
                throw new HrCalendarConflictException("Holiday code already exists in this calendar.");
            row.HolidayDate = data.HolidayDate; row.Code = code; row.Name = data.Name.Trim();
            row.HolidayType = data.HolidayType.Trim(); row.IsPaid = data.IsPaid; row.Notes = data.Notes?.Trim();
        }
        if (id.HasValue) row.Version++; else db.Add(row);
        await Save(ct); return Map(row);
    }

    public async Task<IReadOnlyList<HrWeeklyOffResponse>> WeeklyOffAsync(long businessId, HrOperationalLocationReadScope scope, CancellationToken ct)
    {
        var ids = await AllowedLocations(businessId, scope, ct);
        var rows = await db.Set<HrWeeklyOffPolicyVersion>().AsNoTracking().Include(r => r.Days)
            .Where(r => r.BusinessId == businessId && (ids == null || r.OperationalLocationProjectionId == null
                || Enumerable.Contains(ids, r.OperationalLocationProjectionId.Value)))
            .OrderBy(r => r.ScopeKey).ThenBy(r => r.VersionNo).ToListAsync(ct);
        return rows.Select(Map).ToArray();
    }

    public async Task<HrWeeklyOffResponse> SaveWeeklyOffAsync(long businessId, long? previousId, long? expectedVersion,
        HrWeeklyOffData? data, DateOnly? effectiveTo, CancellationToken ct)
    {
        await Lock(businessId, ct);
        var previous = previousId.HasValue
            ? await db.Set<HrWeeklyOffPolicyVersion>().Include(r => r.Days).SingleOrDefaultAsync(
                r => r.BusinessId == businessId && r.Id == previousId, ct)
                ?? throw new KeyNotFoundException("Weekly-off version not found.") : null;
        if (previous is not null)
        {
            await db.Entry(previous).ReloadAsync(ct); Check(previous.Version, expectedVersion);
            if (!previous.IsActive) throw new HrCalendarConflictException("Weekly-off version is already closed.");
        }
        if (data is null)
        {
            if (previous is null) throw new ArgumentException("A policy version is required.");
            End(previous.EffectiveFrom, previous.EffectiveTo, effectiveTo);
            previous.EffectiveTo = effectiveTo; previous.IsActive = false; previous.Version++;
            await Save(ct); return Map(previous);
        }
        await ValidateLocation(businessId, data.Scope, ct);
        var key = ScopeKey(data.Scope);
        if (data.EffectiveFrom <= Today) throw new ArgumentException("Weekly-off changes must be future-dated.");
        if (previous is not null && (previous.ScopeKey != key || data.EffectiveFrom <= previous.EffectiveFrom
            || data.EffectiveFrom > previous.EffectiveTo))
            throw new HrCalendarConflictException("A successor must retain scope and start within the predecessor period.");
        if (await db.Set<HrWeeklyOffPolicyVersion>().AnyAsync(r => r.BusinessId == businessId && r.ScopeKey == key
            && r.Id != previousId && (data.EffectiveTo == null || r.EffectiveFrom <= data.EffectiveTo)
            && (r.EffectiveTo == null || r.EffectiveTo >= data.EffectiveFrom), ct))
            throw new HrCalendarConflictException("Weekly-off effective periods overlap.");
        var lastVersion = await db.Set<HrWeeklyOffPolicyVersion>().Where(r => r.BusinessId == businessId && r.ScopeKey == key)
            .Select(r => (int?)r.VersionNo).MaxAsync(ct) ?? 0;
        if (previous is not null)
        {
            if (previous.VersionNo != lastVersion) throw new HrCalendarConflictException("Only the latest version can be superseded.");
            previous.EffectiveTo = data.EffectiveFrom.AddDays(-1); previous.IsActive = false; previous.Version++;
        }
        var row = new HrWeeklyOffPolicyVersion
        {
            BusinessId = businessId, ScopeKey = key, ScopeType = data.Scope.ScopeType,
            LocationType = data.Scope.LocationType, OperationalLocationProjectionId = data.Scope.OperationalLocationProjectionId,
            VersionNo = checked(lastVersion + 1), EffectiveFrom = data.EffectiveFrom, EffectiveTo = data.EffectiveTo,
            Days = data.Days.Order().Select(d => new HrWeeklyOffPolicyDay { DayOfWeek = d }).ToList()
        };
        db.Add(row); await Save(ct); return Map(row);
    }

    public async Task<HrCalendarScope> CalendarScopeAsync(long businessId, long id, CancellationToken ct) =>
        Scope(await Calendar(businessId, id, ct));

    public async Task<HrCalendarScope> WeeklyOffScopeAsync(long businessId, long id, CancellationToken ct)
    {
        var row = await db.Set<HrWeeklyOffPolicyVersion>().AsNoTracking()
            .SingleOrDefaultAsync(r => r.BusinessId == businessId && r.Id == id, ct)
            ?? throw new KeyNotFoundException("Weekly-off version not found.");
        return Scope(row);
    }

    public async Task<HrDayClassificationResponse> ClassifyAsync(long businessId, DateOnly date, long locationId, CancellationToken ct)
    {
        var location = await db.OperationalLocationProjections.AsNoTracking()
            .SingleOrDefaultAsync(r => r.BusinessId == businessId && r.Id == locationId, ct)
            ?? throw new KeyNotFoundException("HR operational location not found.");
        var calendars = await db.Set<HrHolidayCalendar>().AsNoTracking().Where(r => r.BusinessId == businessId
            && r.EffectiveFrom <= date && (r.EffectiveTo == null || r.EffectiveTo >= date)
            && (r.ScopeType == "Company" || r.ScopeType == "LocationType" && r.LocationType == location.SourceEntityType
                || r.ScopeType == "Location" && r.OperationalLocationProjectionId == locationId)).ToListAsync(ct);
        var calendarIds = calendars.Select(r => r.Id).ToArray();
        var holidayRows = await db.Set<HrHoliday>().AsNoTracking().Where(h => h.BusinessId == businessId
            && h.IsActive && h.HolidayDate == date && Enumerable.Contains(calendarIds, h.CalendarId)).ToListAsync(ct);
        var holidays = holidayRows.OrderByDescending(h => HrDayClassification.Priority(Scope(calendars.Single(c => c.Id == h.CalendarId))))
            .ThenBy(h => h.Id).Select(Map).ToArray();
        var policies = await db.Set<HrWeeklyOffPolicyVersion>().AsNoTracking().Include(r => r.Days)
            .Where(r => r.BusinessId == businessId && r.EffectiveFrom <= date && (r.EffectiveTo == null || r.EffectiveTo >= date)
                && (r.ScopeType == "Company" || r.ScopeType == "LocationType" && r.LocationType == location.SourceEntityType
                    || r.ScopeType == "Location" && r.OperationalLocationProjectionId == locationId)).ToListAsync(ct);
        var selected = policies.OrderByDescending(p => HrDayClassification.Priority(Scope(p))).FirstOrDefault();
        if (selected is not null && policies.Count(p => p.ScopeKey == selected.ScopeKey) != 1)
            throw new HrCalendarConflictException("Ambiguous effective weekly-off versions.");
        return HrDayClassification.Resolve(date, holidays, selected is null ? null : Map(selected));
    }

    private Task<HrHolidayCalendar> Calendar(long business, long id, CancellationToken ct) =>
        CalendarCore(business, id, ct);
    private async Task<HrHolidayCalendar> CalendarCore(long business, long id, CancellationToken ct) =>
        await db.Set<HrHolidayCalendar>().SingleOrDefaultAsync(r => r.BusinessId == business && r.Id == id, ct)
            ?? throw new KeyNotFoundException("Holiday calendar not found.");

    private async Task ValidateLocation(long business, HrCalendarScope scope, CancellationToken ct)
    {
        if (!scope.OperationalLocationProjectionId.HasValue) return;
        await Organisation.HrOperationalLocationReference.RequireOwnedAsync(db, business, scope.OperationalLocationProjectionId.Value, ct);
    }
    private async Task<long[]?> AllowedLocations(long business, HrOperationalLocationReadScope scope, CancellationToken ct)
    {
        if (scope.BusinessId != business) throw new UnauthorizedAccessException("Location scope Business does not match.");
        if (scope.AllowedSources is null) return null;
        var outlets = scope.AllowedSources.Where(s => s.EntityType == "Outlet").Select(s => s.EntityId).ToArray();
        var warehouses = scope.AllowedSources.Where(s => s.EntityType == "Warehouse").Select(s => s.EntityId).ToArray();
        return await db.OperationalLocationProjections.Where(r => r.BusinessId == business
            && (r.SourceEntityType == "Outlet" && Enumerable.Contains(outlets, r.SourceEntityId)
                || r.SourceEntityType == "Warehouse" && Enumerable.Contains(warehouses, r.SourceEntityId))).Select(r => r.Id).ToArrayAsync(ct);
    }
    private async Task Lock(long business, CancellationToken ct)
    {
        if (db.Database.IsRelational() && db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("HR calendar writes require the audited transaction boundary.");
        if (db.Database.IsNpgsql())
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({business ^ (long.MinValue + 2)})", ct);
    }
    private async Task Save(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException ex) { throw new HrCalendarConflictException("Configuration was changed by another request.", ex); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { throw new HrCalendarConflictException("A configuration with this identity already exists.", ex); }
    }
    private static void Check(long actual, long? expected)
    {
        if (actual != expected) throw new HrCalendarConflictException("Configuration changed since it was read.");
    }
    private void End(DateOnly from, DateOnly? oldEnd, DateOnly? end)
    {
        if (end is null || end <= Today || end < from || oldEnd.HasValue && end > oldEnd)
            throw new ArgumentException("A future end date within the existing effective period is required.");
    }
    private static string ScopeKey(HrCalendarScope scope) => scope.ScopeType switch
    {
        "Company" => "Company", "LocationType" => $"LocationType:{scope.LocationType}",
        "Location" => $"Location:{scope.OperationalLocationProjectionId}",
        _ => throw new ArgumentException("Invalid calendar scope.")
    };
    private static HrCalendarScope Scope(HrHolidayCalendar r) => new(r.ScopeType, r.LocationType, r.OperationalLocationProjectionId);
    private static HrCalendarScope Scope(HrWeeklyOffPolicyVersion r) => new(r.ScopeType, r.LocationType, r.OperationalLocationProjectionId);
    private static HrShiftResponse Map(HrShift r) => new(r.Id, r.BusinessId, new(r.Code, r.Name, r.StartTimeLocal,
        r.EndTimeLocal, r.CrossesMidnight, r.MinimumWorkMinutes, r.MaximumAllowedWorkMinutes,
        r.AllowedEarlyPunchMinutes, r.AllowedLatePunchMinutes, r.MinimumRestBeforeNextShiftMinutes,
        r.GraceInMinutes, r.GraceOutMinutes, r.OvertimeEligible,
        r.Breaks.OrderBy(b => b.Sequence).Select(b => new HrShiftBreakData(b.Sequence, b.DurationMinutes, b.IsPaid)).ToArray()), r.IsActive, r.Version);
    private static HrHolidayCalendarResponse Map(HrHolidayCalendar r) => new(r.Id, r.BusinessId,
        new(r.Name, Scope(r), r.EffectiveFrom, r.EffectiveTo), r.IsActive, r.Version);
    private static HrHolidayResponse Map(HrHoliday r) => new(r.Id, r.CalendarId,
        new(r.HolidayDate, r.Code, r.Name, r.HolidayType, r.IsPaid, r.Notes), r.IsActive, r.Version);
    private static HrWeeklyOffResponse Map(HrWeeklyOffPolicyVersion r) => new(r.Id, r.BusinessId, r.VersionNo,
        new(Scope(r), r.EffectiveFrom, r.EffectiveTo, r.Days.OrderBy(d => d.DayOfWeek).Select(d => d.DayOfWeek).ToArray()), r.IsActive, r.Version);
}
