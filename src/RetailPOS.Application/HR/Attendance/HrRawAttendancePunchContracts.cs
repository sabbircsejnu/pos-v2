using MediatR;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.HR.Organisation;

namespace RetailPOS.Application.HR.Attendance;

public sealed record ManualAttendancePunchInput(long EmployeeId, DateTime PunchTimeLocal, string TimeZoneId,
    string VerificationMethod = "Unknown", string? DirectionCode = null);
public sealed record NormalizedDevicePunchInput(long BusinessId, string DeviceSerial, string DeviceUserId,
    DateTime PunchTimeLocal, string VerificationMethod = "Unknown", string? DirectionCode = null);
public sealed record RawAttendancePunchResponse(long Id, long BusinessId, long? DeviceId, string? DeviceSerialSnapshot,
    string? DeviceUserId, long? EmployeeId, DateTime PunchTimeLocal, DateTimeOffset? PunchTimeUtc,
    string? TimeZoneId, string VerificationMethod, string? DirectionCode, DateTimeOffset ReceivedAtUtc,
    string SourceType, string ProcessingStatus, string? ProcessingError, int ProcessingVersion,
    string IdempotencyFingerprint, bool Duplicate);
public sealed record RawAttendancePunchListItem(long Id, long? DeviceId, string? DeviceSerialSnapshot,
    string? DeviceUserId, long? EmployeeId, DateTime PunchTimeLocal, DateTimeOffset? PunchTimeUtc,
    string? TimeZoneId, string VerificationMethod, string? DirectionCode, DateTimeOffset ReceivedAtUtc,
    string SourceType, string ProcessingStatus, string? ProcessingError, int ProcessingVersion);

public interface IHrRawAttendancePunchStore
{
    Task<IReadOnlyList<long>> PrimaryLocationsAsync(long businessId, long employeeId, DateOnly date, CancellationToken ct);
    Task<RawAttendancePunchResponse> AddManualAsync(long businessId, ManualAttendancePunchInput input,
        HrOperationalLocationReadScope scope, CancellationToken ct);
    Task<IReadOnlyList<RawAttendancePunchListItem>> ListAsync(long businessId, DateTimeOffset fromUtc,
        DateTimeOffset toUtc, HrOperationalLocationReadScope scope, CancellationToken ct);
}

public interface ITrustedNormalizedAttendancePunchIngestor
{
    Task<RawAttendancePunchResponse> IngestDeviceAsync(NormalizedDevicePunchInput punch, CancellationToken ct);
}

public sealed record AddManualAttendancePunchCommand(ManualAttendancePunchInput Data) : IRequest<RawAttendancePunchResponse>;
public sealed record GetRawAttendancePunchesQuery(DateTimeOffset FromUtc, DateTimeOffset ToUtc) : IRequest<IReadOnlyList<RawAttendancePunchListItem>>;

public sealed class HrRawAttendancePunchHandlers(IHrOrganisationAccess access, IHrRawAttendancePunchStore store,
    IRequiredAuditTransaction audit) : IRequestHandler<AddManualAttendancePunchCommand, RawAttendancePunchResponse>,
        IRequestHandler<GetRawAttendancePunchesQuery, IReadOnlyList<RawAttendancePunchListItem>>
{
    public async Task<RawAttendancePunchResponse> Handle(AddManualAttendancePunchCommand request, CancellationToken ct)
    {
        Validate(request.Data);
        var business = access.RequireBusinessId();
        await access.EnsureAsync(business, HrOrganisationOperation.CreateManualAttendancePunch, null, ct);
        var localDate = DateOnly.FromDateTime(DateTime.SpecifyKind(request.Data.PunchTimeLocal, DateTimeKind.Unspecified));
        var locations = await store.PrimaryLocationsAsync(business, request.Data.EmployeeId, localDate, ct);
        if (locations.Count == 0) throw new KeyNotFoundException("Employee has no effective Primary location on the punch date.");
        foreach (var location in locations) await access.EnsureLocationAccessibleAsync(business, location, ct);
        access.RequireRealUserId();
        var scope = await access.GetLocationReadScopeAsync(business, ct);
        return await audit.ExecuteAsync(new RequiredAuditOperation(business, "hr.attendance.manual_punch_recorded", "HR",
            "Recorded a manually entered attendance punch.", "HrRawAttendancePunch", "manual"),
            t => store.AddManualAsync(business, request.Data, scope, t), ct);
    }

    public async Task<IReadOnlyList<RawAttendancePunchListItem>> Handle(GetRawAttendancePunchesQuery request, CancellationToken ct)
    {
        if (request.FromUtc == default || request.ToUtc <= request.FromUtc || request.ToUtc - request.FromUtc > TimeSpan.FromDays(31))
            throw new ArgumentException("A valid UTC interval of at most 31 days is required.");
        var business = access.RequireBusinessId();
        await access.EnsureAsync(business, HrOrganisationOperation.ViewRawAttendancePunches, null, ct);
        return await store.ListAsync(business, request.FromUtc, request.ToUtc,
            await access.GetLocationReadScopeAsync(business, ct), ct);
    }

    internal static void Validate(ManualAttendancePunchInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.EmployeeId <= 0 || input.PunchTimeLocal == default
            || input.PunchTimeLocal.Kind != DateTimeKind.Unspecified)
            throw new ArgumentException("Employee and an unspecified local punch timestamp are required.");
        if (string.IsNullOrWhiteSpace(input.TimeZoneId) || input.TimeZoneId.Length > 100)
            throw new ArgumentException("A valid local time-zone ID is required.");
        if (input.VerificationMethod is not ("Face" or "Fingerprint" or "Card" or "Password" or "Unknown" or "Manual"))
            throw new ArgumentException("Unsupported verification method.");
        if (input.DirectionCode is { Length: > 40 })
            throw new ArgumentException("Direction code is too long.");
        try { _ = TimeZoneInfo.FindSystemTimeZoneById(input.TimeZoneId); }
        catch (TimeZoneNotFoundException ex) { throw new ArgumentException("Time-zone ID is not recognized.", ex); }
        catch (InvalidTimeZoneException ex) { throw new ArgumentException("Time-zone definition is invalid.", ex); }
    }

}

public static class HrPunchTimeResolver
{
    public static DateTimeOffset? ResolveUtc(DateTime local, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local) || zone.IsAmbiguousTime(local)) return null;
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
    }
}
