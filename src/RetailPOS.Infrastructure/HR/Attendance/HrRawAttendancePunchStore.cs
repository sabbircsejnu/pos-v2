using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using RetailPOS.Application.HR.Attendance;
using RetailPOS.Application.HR.Organisation;
using RetailPOS.Core.Entities.HR.Attendance;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.HR.Organisation;

namespace RetailPOS.Infrastructure.HR.Attendance;

public sealed class HrRawAttendancePunchStore(RetailPOSDbContext db, TimeProvider clock) : IHrRawAttendancePunchStore,
    ITrustedNormalizedAttendancePunchIngestor
{
    public async Task<IReadOnlyList<long>> PrimaryLocationsAsync(long businessId, long employeeId, DateOnly date,
        CancellationToken ct) =>
        await (from assignment in db.HrEmployeeLocationAssignments.AsNoTracking()
               join employee in db.HrEmployees.AsNoTracking() on new { assignment.BusinessId, assignment.EmployeeId }
                   equals new { employee.BusinessId, EmployeeId = employee.Id }
               join employment in db.HrEmploymentPeriods.AsNoTracking() on new { assignment.BusinessId, assignment.EmployeeId }
                   equals new { employment.BusinessId, employment.EmployeeId }
               where assignment.BusinessId == businessId && assignment.EmployeeId == employeeId
                   && assignment.AssignmentType == "Primary" && assignment.EffectiveFrom <= date
                   && (assignment.EffectiveTo == null || assignment.EffectiveTo >= date)
                   && employment.StartDate <= date && (employment.EndDate == null || employment.EndDate >= date)
               select assignment.OperationalLocationProjectionId).Distinct().ToArrayAsync(ct);

    public async Task<RawAttendancePunchResponse> AddManualAsync(long businessId, ManualAttendancePunchInput input,
        HrOperationalLocationReadScope scope, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Manual punch writes require the audited transaction boundary.");
        await Lock(businessId, ct);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(input.TimeZoneId);
        var utc = HrPunchTimeResolver.ResolveUtc(input.PunchTimeLocal, zone)
            ?? throw new ArgumentException("The local punch time is ambiguous or does not exist in the selected time zone.");
        var date = DateOnly.FromDateTime(input.PunchTimeLocal);
        var locations = await PrimaryLocationsAsync(businessId, input.EmployeeId, date, ct);
        if (locations.Count == 0) throw new KeyNotFoundException("Employee is not active at an effective Primary location on the punch date.");
        if (!await db.HrEmployees.AnyAsync(e => e.BusinessId == businessId && e.Id == input.EmployeeId
            && e.CurrentEmploymentStatus == "Active", ct))
            throw new KeyNotFoundException("Employee is not active.");
        foreach (var location in locations)
            await HrOperationalLocationReference.EnsureAuthorizedAsync(db, businessId, location, scope, ct);
        var fingerprint = CreateFingerprint(businessId, "Manual", input.EmployeeId, null, null,
            utc, input.PunchTimeLocal);
        return await InsertOrReturnAsync(new HrRawAttendancePunch
        {
            BusinessId = businessId,
            EmployeeId = input.EmployeeId,
            PunchTimeLocal = DateTime.SpecifyKind(input.PunchTimeLocal, DateTimeKind.Unspecified),
            PunchTimeUtc = utc.UtcDateTime,
            TimeZoneId = input.TimeZoneId,
            VerificationMethod = input.VerificationMethod,
            DirectionCode = input.DirectionCode?.Trim(),
            ReceivedAtUtc = clock.GetUtcNow().UtcDateTime,
            IdempotencyFingerprint = fingerprint,
            SourceType = "Manual",
            ProcessingStatus = "Pending",
            ProcessingVersion = 0
        }, ct);
    }

    public async Task<RawAttendancePunchResponse> IngestDeviceAsync(NormalizedDevicePunchInput punch, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(punch);
        if (punch.BusinessId <= 0 || string.IsNullOrWhiteSpace(punch.DeviceSerial) || punch.DeviceSerial.Length > 150
            || string.IsNullOrWhiteSpace(punch.DeviceUserId) || punch.DeviceUserId.Length > 100
            || punch.PunchTimeLocal == default || punch.PunchTimeLocal.Kind != DateTimeKind.Unspecified
            || !ValidVerification(punch.VerificationMethod) || punch.DirectionCode is { Length: > 40 })
            throw new ArgumentException("Normalized device punch is invalid.");

        await using var ownTransaction = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(ct) : null;
        await Lock(punch.BusinessId, ct);
        var device = await db.Set<HrAttendanceDevice>().SingleOrDefaultAsync(d => d.BusinessId == punch.BusinessId
            && d.SerialNumber == punch.DeviceSerial.Trim() && d.Status == "Active", ct)
            ?? throw new KeyNotFoundException("Active device was not found in the requested Business.");
        var zone = TimeZoneInfo.FindSystemTimeZoneById(device.DeviceTimeZoneId);
        var utc = HrPunchTimeResolver.ResolveUtc(punch.PunchTimeLocal, zone);
        long? employeeId = null;
        var error = utc is null ? "Device-local time is ambiguous or invalid." : null;
        if (utc.HasValue)
        {
            var mapping = await db.Set<HrDeviceUserMapping>().AsNoTracking().Where(m => m.BusinessId == punch.BusinessId
                && m.DeviceId == device.Id && m.DeviceUserId == punch.DeviceUserId
                && m.EffectiveFromUtc <= utc.Value.UtcDateTime
                && (m.EffectiveToUtc == null || m.EffectiveToUtc > utc.Value.UtcDateTime))
                .Select(m => (long?)m.EmployeeId).SingleOrDefaultAsync(ct);
            employeeId = mapping;
        }
        var fingerprint = CreateFingerprint(punch.BusinessId, "Device", employeeId, device.Id,
            punch.DeviceUserId.Trim(), utc, punch.PunchTimeLocal);
        var response = await InsertOrReturnAsync(new HrRawAttendancePunch
        {
            BusinessId = punch.BusinessId,
            DeviceId = device.Id,
            DeviceSerialSnapshot = device.SerialNumber,
            DeviceUserId = punch.DeviceUserId.Trim(),
            EmployeeId = employeeId,
            PunchTimeLocal = DateTime.SpecifyKind(punch.PunchTimeLocal, DateTimeKind.Unspecified),
            PunchTimeUtc = utc?.UtcDateTime,
            TimeZoneId = device.DeviceTimeZoneId,
            VerificationMethod = punch.VerificationMethod,
            DirectionCode = punch.DirectionCode?.Trim(),
            ReceivedAtUtc = clock.GetUtcNow().UtcDateTime,
            IdempotencyFingerprint = fingerprint,
            SourceType = "Device",
            ProcessingStatus = utc is null ? "Failed" : employeeId.HasValue ? "Pending" : "Unmapped",
            ProcessingError = error,
            ProcessingVersion = 0
        }, ct);
        if (ownTransaction is not null) await ownTransaction.CommitAsync(ct);
        return response;
    }

    public async Task<IReadOnlyList<RawAttendancePunchListItem>> ListAsync(long businessId, DateTimeOffset fromUtc,
        DateTimeOffset toUtc, HrOperationalLocationReadScope scope, CancellationToken ct)
    {
        if (scope.BusinessId != businessId) throw new UnauthorizedAccessException("Punch query is outside Business scope.");
        var rows = await db.Set<HrRawAttendancePunch>().AsNoTracking().Where(p => p.BusinessId == businessId
            && p.ReceivedAtUtc >= fromUtc.UtcDateTime && p.ReceivedAtUtc < toUtc.UtcDateTime)
            .OrderByDescending(p => p.ReceivedAtUtc).Take(1000).ToListAsync(ct);
        if (scope.AllowedSources is not null)
        {
            var allowed = await db.OperationalLocationProjections.AsNoTracking().Where(p => p.BusinessId == businessId)
                .Select(p => new { p.Id, p.SourceEntityType, p.SourceEntityId }).ToListAsync(ct);
            var allowedIds = allowed.Where(p => scope.AllowedSources.Contains(new(p.SourceEntityType, p.SourceEntityId)))
                .Select(p => p.Id).ToHashSet();
            var deviceLocations = await db.Set<HrAttendanceDevice>().AsNoTracking().Where(d => d.BusinessId == businessId)
                .Select(d => new { d.Id, d.OperationalLocationProjectionId }).ToDictionaryAsync(d => d.Id,
                    d => d.OperationalLocationProjectionId, ct);
            var visible = new HashSet<long>();
            foreach (var punch in rows.Where(p => p.EmployeeId.HasValue))
            {
                var date = DateOnly.FromDateTime(punch.PunchTimeLocal);
                var assigned = await PrimaryLocationsAsync(businessId, punch.EmployeeId!.Value, date, ct);
                if (assigned.Any(allowedIds.Contains)) visible.Add(punch.Id);
            }
            rows = rows.Where(p => p.EmployeeId.HasValue ? visible.Contains(p.Id)
                : p.DeviceId.HasValue && deviceLocations.TryGetValue(p.DeviceId.Value, out var location) && allowedIds.Contains(location)).ToList();
        }
        return rows.Select(MapList).ToArray();
    }

    private async Task<RawAttendancePunchResponse> InsertOrReturnAsync(HrRawAttendancePunch entity, CancellationToken ct)
    {
        var existing = await db.Set<HrRawAttendancePunch>().AsNoTracking()
            .SingleOrDefaultAsync(p => p.IdempotencyFingerprint == entity.IdempotencyFingerprint, ct);
        if (existing is not null) return Map(existing, true);
        db.Add(entity);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            if (db.Database.CurrentTransaction is not null)
                throw new InvalidOperationException("Punch fingerprint race escaped the Business idempotency lock.", ex);
            throw;
        }
        return Map(entity, false);
    }

    private async Task Lock(long businessId, CancellationToken ct)
    {
        if (!db.Database.IsRelational()) return;
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Punch writes require a transaction.");
        if (db.Database.IsNpgsql())
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({businessId ^ (long.MinValue + 2)})", ct);
    }

    private static bool ValidVerification(string value) => value is "Face" or "Fingerprint" or "Card" or "Password" or "Unknown" or "Manual";
    private static string CreateFingerprint(long businessId, string source, long? employeeId, long? deviceId,
        string? deviceUserId, DateTimeOffset? punchTimeUtc, DateTime punchTimeLocal)
    {
        var instant = punchTimeUtc.HasValue ? punchTimeUtc.Value.UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : punchTimeLocal.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var identity = source == "Device"
            ? $"{deviceId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? ""}\n{deviceUserId ?? ""}"
            : employeeId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "";
        var value = string.Join('\n', businessId, source, identity, instant);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }
    private static DateTimeOffset? AsOffset(DateTime? value) => value is null ? null
        : new DateTimeOffset(value.Value.Kind == DateTimeKind.Utc ? value.Value : value.Value.ToUniversalTime());
    private static DateTimeOffset AsOffset(DateTime value) =>
        new(value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime());
    private static RawAttendancePunchResponse Map(HrRawAttendancePunch p, bool duplicate) => new(p.Id, p.BusinessId,
        p.DeviceId, p.DeviceSerialSnapshot, p.DeviceUserId, p.EmployeeId, p.PunchTimeLocal, AsOffset(p.PunchTimeUtc),
        p.TimeZoneId, p.VerificationMethod, p.DirectionCode, AsOffset(p.ReceivedAtUtc), p.SourceType,
        p.ProcessingStatus, p.ProcessingError, p.ProcessingVersion, p.IdempotencyFingerprint, duplicate);
    private static RawAttendancePunchListItem MapList(HrRawAttendancePunch p) => new(p.Id, p.DeviceId,
        p.DeviceSerialSnapshot, p.DeviceUserId, p.EmployeeId, p.PunchTimeLocal, AsOffset(p.PunchTimeUtc),
        p.TimeZoneId, p.VerificationMethod, p.DirectionCode, AsOffset(p.ReceivedAtUtc), p.SourceType,
        p.ProcessingStatus, p.ProcessingError, p.ProcessingVersion);
}
