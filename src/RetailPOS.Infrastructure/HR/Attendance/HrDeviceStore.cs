using Microsoft.EntityFrameworkCore;
using Npgsql;
using RetailPOS.Application.HR.Attendance;
using RetailPOS.Application.HR.Calendars;
using RetailPOS.Application.HR.Organisation;
using RetailPOS.Core.Entities.HR.Attendance;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.HR.Attendance;

public sealed class HrDeviceStore(RetailPOSDbContext db, TimeProvider clock) : IHrDeviceStore
{
    public async Task<IReadOnlyList<HrDeviceResponse>> ListAsync(long b, HrOperationalLocationReadScope scope, CancellationToken ct)
    {
        if (b != scope.BusinessId) throw new UnauthorizedAccessException("Device scope is outside Business.");
        var query = db.Set<HrAttendanceDevice>().AsNoTracking().Where(d => d.BusinessId == b);
        if (scope.AllowedSources is not null)
        {
            var sources = scope.AllowedSources.ToHashSet();
            var projections = await db.OperationalLocationProjections.AsNoTracking().Where(p => p.BusinessId == b)
                .Select(p => new { p.Id, p.SourceEntityType, p.SourceEntityId }).ToListAsync(ct);
            var ids = projections.Where(p => sources.Contains(new(p.SourceEntityType, p.SourceEntityId))).Select(p => p.Id).ToArray();
            query = query.Where(d => Enumerable.Contains(ids, d.OperationalLocationProjectionId));
        }
        return (await query.OrderBy(d => d.DeviceCode).ToListAsync(ct)).Select(Map).ToArray();
    }
    public async Task<HrDeviceResponse> DeviceAsync(long b, long id, CancellationToken ct) => Map(await Find(b, id, ct));
    public async Task<HrDeviceResponse> SaveAsync(long b, long? id, long? version, HrDeviceData d, bool replaceCredentialReference, HrOperationalLocationReadScope scope, CancellationToken ct)
    {
        HrDeviceHandlers.Validate(d); await Lock(b, ct);
        var row = id.HasValue ? await Find(b, id.Value, ct) : new HrAttendanceDevice { BusinessId = b };
        if (id.HasValue) { await db.Entry(row).ReloadAsync(ct); Check(row.Version, version); }
        await Organisation.HrOperationalLocationReference.EnsureAuthorizedAsync(db, b, d.OperationalLocationProjectionId, scope, ct);
        if (id.HasValue) await Organisation.HrOperationalLocationReference.EnsureAuthorizedAsync(db, b, row.OperationalLocationProjectionId, scope, ct);
        var vendor = d.VendorCode.Trim().ToUpperInvariant(); var serial = d.SerialNumber.Trim(); var code = d.DeviceCode.Trim().ToUpperInvariant();
        if (await db.Set<HrAttendanceDevice>().AnyAsync(x => x.Id != row.Id
            && (x.VendorCode == vendor && x.SerialNumber == serial || x.BusinessId == b && x.DeviceCode == code), ct))
            throw new HrCalendarConflictException("Device identity/code is already registered.");
        if (id.HasValue && await db.Set<HrDeviceUserMapping>().AnyAsync(m => m.DeviceId == row.Id, ct)
            && (row.SerialNumber != serial || row.VendorCode != vendor || row.OperationalLocationProjectionId != d.OperationalLocationProjectionId
                || row.DeviceTimeZoneId != d.DeviceTimeZoneId || row.ProtocolType != d.ProtocolType))
            throw new HrCalendarConflictException("Mapped device identity, timezone, protocol and location are immutable; register a new device.");
        await ValidateLocation(b, d.OperationalLocationProjectionId, ct);
        row.DeviceCode = code; row.SerialNumber = serial; row.VendorCode = vendor; row.Model = d.Model.Trim();
        row.OperationalLocationProjectionId = d.OperationalLocationProjectionId; row.ProtocolType = d.ProtocolType;
        row.DeviceTimeZoneId = d.DeviceTimeZoneId; row.Status = d.Status; row.ConnectionHost = d.ConnectionHost; row.ConnectionPort = d.ConnectionPort;
        row.FirmwareVersion = d.FirmwareVersion;
        if (!id.HasValue || replaceCredentialReference) row.CredentialSecretReference = d.CredentialSecretReference;
        if (id.HasValue) row.Version++; else db.Add(row);
        await Save(ct); return Map(row);
    }
    public async Task<IReadOnlyList<HrDeviceMappingResponse>> MappingsAsync(long b, long deviceId, CancellationToken ct)
    {
        await Find(b, deviceId, ct);
        return (await db.Set<HrDeviceUserMapping>().AsNoTracking().Where(m => m.BusinessId == b && m.DeviceId == deviceId)
            .OrderBy(m => m.DeviceUserId).ThenBy(m => m.EffectiveFromUtc).ToListAsync(ct)).Select(Map).ToArray();
    }
    public async Task<HrDeviceMappingResponse> MappingAsync(long b, long id, CancellationToken ct) =>
        Map(await db.Set<HrDeviceUserMapping>().SingleOrDefaultAsync(m => m.BusinessId == b && m.Id == id, ct)
            ?? throw new KeyNotFoundException("Device mapping not found."));
    public async Task<IReadOnlyList<long>> EmployeePrimaryLocationsAsync(long b, long employeeId, DateOnly date, CancellationToken ct)
    {
        if (!await db.HrEmployees.AnyAsync(e => e.BusinessId == b && e.Id == employeeId, ct)) throw new KeyNotFoundException("Employee not found.");
        return await db.HrEmployeeLocationAssignments.AsNoTracking().Where(a => a.BusinessId == b && a.EmployeeId == employeeId
            && a.AssignmentType == "Primary" && a.EffectiveFrom <= date && (a.EffectiveTo == null || a.EffectiveTo >= date)
            && a.Status != "Cancelled").Select(a => a.OperationalLocationProjectionId).Distinct().ToArrayAsync(ct);
    }
    public async Task<HrDeviceMappingResponse> CreateMappingAsync(long b, long deviceId, HrDeviceMappingData data, HrOperationalLocationReadScope scope, CancellationToken ct)
    {
        await Lock(b, ct);
        var device = await Find(b, deviceId, ct); await db.Entry(device).ReloadAsync(ct);
        await Organisation.HrOperationalLocationReference.EnsureAuthorizedAsync(db, b, device.OperationalLocationProjectionId, scope, ct);
        if (device.Status != "Active") throw new HrCalendarConflictException("Only active devices can receive new user mappings.");
        await ValidateLocation(b, device.OperationalLocationProjectionId, ct);
        if (db.Database.IsNpgsql()) await db.Database.SqlQuery<long>(
            $"SELECT id AS \"Value\" FROM hr_employees WHERE business_id = {b} AND id = {data.EmployeeId} FOR UPDATE").ToListAsync(ct);
        var tz = TimeZoneInfo.FindSystemTimeZoneById(device.DeviceTimeZoneId);
        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(data.EffectiveFromUtc, tz).DateTime);
        if (scope.AllowedSources is not null)
        {
            var primary = await EmployeePrimaryLocationsAsync(b, data.EmployeeId, date, ct);
            if (primary.Count == 0) throw new UnauthorizedAccessException("Employee has no authorized Primary location context.");
            foreach (var location in primary)
                await Organisation.HrOperationalLocationReference.EnsureAuthorizedAsync(db, b, location, scope, ct);
        }
        if (!await db.HrEmploymentPeriods.AnyAsync(p => p.BusinessId == b && p.EmployeeId == data.EmployeeId && p.StartDate <= date
            && (p.EndDate == null || p.EndDate >= date), ct))
            throw new ArgumentException("Employee must be employed at mapping start.");
        if (!await db.HrEmployeeLocationAssignments.AnyAsync(a => a.BusinessId == b && a.EmployeeId == data.EmployeeId
            && a.OperationalLocationProjectionId == device.OperationalLocationProjectionId && a.EffectiveFrom <= date
            && (a.EffectiveTo == null || a.EffectiveTo >= date) && a.Status != "Cancelled", ct))
            throw new ArgumentException("Employee must have a historical/effective assignment at the device location.");
        var from = data.EffectiveFromUtc.UtcDateTime; var to = data.EffectiveToUtc?.UtcDateTime;
        var user = data.DeviceUserId.Trim();
        if (await db.Set<HrDeviceUserMapping>().AnyAsync(m => m.BusinessId == b && m.DeviceId == deviceId && m.DeviceUserId == user
            && (to == null || m.EffectiveFromUtc < to) && (m.EffectiveToUtc == null || m.EffectiveToUtc > from), ct))
            throw new HrCalendarConflictException("Device/user mapping interval overlaps retained history.");
        var row = new HrDeviceUserMapping { BusinessId = b, DeviceId = deviceId, EmployeeId = data.EmployeeId,
            DeviceUserId = user, EffectiveFromUtc = from, EffectiveToUtc = to, IsActive = to is null };
        db.Add(row); await Save(ct); return Map(row);
    }
    public async Task<HrDeviceMappingResponse> CloseMappingAsync(long b, long id, long version, DateTimeOffset end, CancellationToken ct)
    {
        await Lock(b, ct);
        var row = await db.Set<HrDeviceUserMapping>().SingleOrDefaultAsync(m => m.BusinessId == b && m.Id == id, ct)
            ?? throw new KeyNotFoundException("Mapping not found.");
        await db.Entry(row).ReloadAsync(ct); Check(row.Version, version);
        var utc = end.UtcDateTime;
        if (!row.IsActive || utc <= AsUtc(row.EffectiveFromUtc) || utc < clock.GetUtcNow().UtcDateTime)
            throw new HrCalendarConflictException("An open mapping can only be closed now or in the future, after its start.");
        row.EffectiveToUtc = utc; row.IsActive = false; row.Version++; await Save(ct); return Map(row);
    }
    private async Task ValidateLocation(long b, long id, CancellationToken ct)
    {
        await Organisation.HrOperationalLocationReference.RequireOwnedAsync(db, b, id, ct);
    }
    private async Task Lock(long b, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Device writes require an audited transaction.");
        if (db.Database.IsNpgsql()) await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({b ^ (long.MinValue + 2)})", ct);
    }
    private async Task<HrAttendanceDevice> Find(long b, long id, CancellationToken ct) =>
        await db.Set<HrAttendanceDevice>().SingleOrDefaultAsync(d => d.BusinessId == b && d.Id == id, ct)
            ?? throw new KeyNotFoundException("Device not found.");
    private static void Check(long actual, long? expected) {
        if (actual != expected) throw new HrCalendarConflictException("Device/mapping changed since it was read."); }
    private async Task Save(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException e) { throw new HrCalendarConflictException("Device configuration changed concurrently.", e); }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { throw new HrCalendarConflictException("Device identity or active mapping already exists.", e); }
    }
    private static DateTime AsUtc(DateTime d) => d.Kind == DateTimeKind.Local ? d.ToUniversalTime() : DateTime.SpecifyKind(d, DateTimeKind.Utc);
    private static HrDeviceResponse Map(HrAttendanceDevice d) => new(d.Id, d.BusinessId, d.DeviceCode, d.SerialNumber, d.VendorCode,
        d.Model, d.OperationalLocationProjectionId, d.ProtocolType, d.DeviceTimeZoneId, d.Status, d.ConnectionHost,
        d.ConnectionPort, d.FirmwareVersion, d.CredentialSecretReference is not null, d.LastSeenAtUtc.HasValue ? new DateTimeOffset(AsUtc(d.LastSeenAtUtc.Value)) : null, d.Version);
    private static HrDeviceMappingResponse Map(HrDeviceUserMapping m) => new(m.Id, m.BusinessId, m.DeviceId, m.EmployeeId,
        m.DeviceUserId, new(AsUtc(m.EffectiveFromUtc)), m.EffectiveToUtc.HasValue ? new DateTimeOffset(AsUtc(m.EffectiveToUtc.Value)) : null, m.IsActive, m.Version);
}
