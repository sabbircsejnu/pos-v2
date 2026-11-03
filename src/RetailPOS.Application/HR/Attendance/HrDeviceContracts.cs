using System.Text.RegularExpressions;
using MediatR;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.HR.Organisation;

namespace RetailPOS.Application.HR.Attendance;

/// <summary>Device metadata. CredentialSecretReference names an externally managed secret, never a credential value.</summary>
public sealed record HrDeviceData(string DeviceCode, string SerialNumber, string VendorCode, string Model,
    long OperationalLocationProjectionId, string ProtocolType, string DeviceTimeZoneId,
    string Status = "Active", string? ConnectionHost = null, int? ConnectionPort = null,
    string? FirmwareVersion = null, string? CredentialSecretReference = null);
/// <summary>Non-secret device configuration and optimistic concurrency metadata.</summary>
public sealed record HrDeviceResponse(long Id, long BusinessId, string DeviceCode, string SerialNumber,
    string VendorCode, string Model, long OperationalLocationProjectionId, string ProtocolType,
    string DeviceTimeZoneId, string Status, string? ConnectionHost, int? ConnectionPort, string? FirmwareVersion,
    bool HasCredentialReference, DateTimeOffset? LastSeenAtUtc, long Version);
/// <summary>UTC-effective employee/device user mapping; history is never deleted.</summary>
public sealed record HrDeviceMappingData(long EmployeeId, string DeviceUserId, DateTimeOffset EffectiveFromUtc,
    DateTimeOffset? EffectiveToUtc = null);
/// <summary>A retained device mapping. IsActive indicates an open mapping, not historical eligibility.</summary>
public sealed record HrDeviceMappingResponse(long Id, long BusinessId, long DeviceId, long EmployeeId,
    string DeviceUserId, DateTimeOffset EffectiveFromUtc, DateTimeOffset? EffectiveToUtc, bool IsActive, long Version);

public interface IHrDeviceStore
{
    Task<IReadOnlyList<HrDeviceResponse>> ListAsync(long b, HrOperationalLocationReadScope scope, CancellationToken ct);
    Task<HrDeviceResponse> DeviceAsync(long b, long id, CancellationToken ct);
    Task<HrDeviceResponse> SaveAsync(long b, long? id, long? version, HrDeviceData data, bool replaceCredentialReference, HrOperationalLocationReadScope scope, CancellationToken ct);
    Task<IReadOnlyList<HrDeviceMappingResponse>> MappingsAsync(long b, long deviceId, CancellationToken ct);
    Task<HrDeviceMappingResponse> MappingAsync(long b, long id, CancellationToken ct);
    Task<HrDeviceMappingResponse> CreateMappingAsync(long b, long deviceId, HrDeviceMappingData data, HrOperationalLocationReadScope scope, CancellationToken ct);
    Task<HrDeviceMappingResponse> CloseMappingAsync(long b, long id, long version, DateTimeOffset end, CancellationToken ct);
    Task<IReadOnlyList<long>> EmployeePrimaryLocationsAsync(long b, long employeeId, DateOnly at, CancellationToken ct);
}

public sealed record GetHrDevicesQuery : IRequest<IReadOnlyList<HrDeviceResponse>>;
public sealed record GetHrDeviceQuery(long Id) : IRequest<HrDeviceResponse>;
public sealed record SaveHrDeviceCommand(long? Id, long? ExpectedVersion, HrDeviceData Data, bool ReplaceCredentialReference = false) : IRequest<HrDeviceResponse>;
public sealed record DeactivateHrDeviceCommand(long Id, long ExpectedVersion) : IRequest<HrDeviceResponse>;
public sealed record GetHrDeviceMappingsQuery(long DeviceId) : IRequest<IReadOnlyList<HrDeviceMappingResponse>>;
public sealed record CreateHrDeviceMappingCommand(long DeviceId, HrDeviceMappingData Data) : IRequest<HrDeviceMappingResponse>;
public sealed record CloseHrDeviceMappingCommand(long Id, long ExpectedVersion, DateTimeOffset EffectiveToUtc) : IRequest<HrDeviceMappingResponse>;

public sealed class HrDeviceHandlers(IHrOrganisationAccess access, IHrDeviceStore store, IRequiredAuditTransaction audit) :
    IRequestHandler<GetHrDevicesQuery, IReadOnlyList<HrDeviceResponse>>,
    IRequestHandler<GetHrDeviceQuery, HrDeviceResponse>,
    IRequestHandler<SaveHrDeviceCommand, HrDeviceResponse>,
    IRequestHandler<DeactivateHrDeviceCommand, HrDeviceResponse>,
    IRequestHandler<GetHrDeviceMappingsQuery, IReadOnlyList<HrDeviceMappingResponse>>,
    IRequestHandler<CreateHrDeviceMappingCommand, HrDeviceMappingResponse>,
    IRequestHandler<CloseHrDeviceMappingCommand, HrDeviceMappingResponse>
{
    public async Task<IReadOnlyList<HrDeviceResponse>> Handle(GetHrDevicesQuery r, CancellationToken ct)
    {
        var b = access.RequireBusinessId(); await access.EnsureAsync(b, HrOrganisationOperation.ViewDevices, null, ct);
        return await store.ListAsync(b, await access.GetLocationReadScopeAsync(b, ct), ct);
    }
    public async Task<HrDeviceResponse> Handle(GetHrDeviceQuery r, CancellationToken ct)
    {
        var b = access.RequireBusinessId();
        await access.EnsureAsync(b, HrOrganisationOperation.ViewDevices, null, ct);
        return await DeviceScope(b, r.Id, HrOrganisationOperation.ViewDevices, ct);
    }
    public async Task<HrDeviceResponse> Handle(SaveHrDeviceCommand r, CancellationToken ct)
    {
        Validate(r.Data);
        if (r.Id is <= 0 || r.ExpectedVersion is <= 0 || r.Id.HasValue != r.ExpectedVersion.HasValue)
            throw new ArgumentException("Device identity and expected version must be supplied together.");
        var b = access.RequireBusinessId(); await access.EnsureAsync(b, HrOrganisationOperation.ManageDevices, r.Data.OperationalLocationProjectionId, ct);
        if (r.Id.HasValue) await DeviceScope(b, r.Id.Value, HrOrganisationOperation.ManageDevices, ct);
        access.RequireRealUserId();
        var scope = await access.GetLocationReadScopeAsync(b, ct);
        return await audit.ExecuteAsync(new RequiredAuditOperation(b, r.Id.HasValue ? "hr.device.updated" : "hr.device.created",
            "HR", "Saved non-secret attendance device configuration.", "HrAttendanceDevice", r.Id?.ToString() ?? "new"),
            t => store.SaveAsync(b, r.Id, r.ExpectedVersion, r.Data, r.ReplaceCredentialReference, scope, t), ct);
    }
    public async Task<IReadOnlyList<HrDeviceMappingResponse>> Handle(GetHrDeviceMappingsQuery r, CancellationToken ct)
    {
        var b = access.RequireBusinessId(); await DeviceScope(b, r.DeviceId, HrOrganisationOperation.ViewDeviceMappings, ct);
        return await store.MappingsAsync(b, r.DeviceId, ct);
    }
    public async Task<HrDeviceResponse> Handle(DeactivateHrDeviceCommand r, CancellationToken ct)
    {
        var b = access.RequireBusinessId(); var d = await DeviceScope(b, r.Id, HrOrganisationOperation.ManageDevices, ct);
        return await Handle(new SaveHrDeviceCommand(r.Id, r.ExpectedVersion, new(d.DeviceCode, d.SerialNumber, d.VendorCode,
            d.Model, d.OperationalLocationProjectionId, d.ProtocolType, d.DeviceTimeZoneId, "Disabled", d.ConnectionHost,
            d.ConnectionPort, d.FirmwareVersion)), ct);
    }
    public async Task<HrDeviceMappingResponse> Handle(CreateHrDeviceMappingCommand r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r.Data);
        Text(r.Data.DeviceUserId, 100, "Device user ID");
        if (r.Data.EmployeeId <= 0 || r.Data.EffectiveFromUtc == default || r.Data.EffectiveToUtc <= r.Data.EffectiveFromUtc)
            throw new ArgumentException("Employee and a positive effective mapping interval are required.");
        var b = access.RequireBusinessId(); var device = await DeviceScope(b, r.DeviceId, HrOrganisationOperation.ManageDeviceMappings, ct);
        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(r.Data.EffectiveFromUtc,
            TimeZoneInfo.FindSystemTimeZoneById(device.DeviceTimeZoneId)).DateTime);
        var locations = await store.EmployeePrimaryLocationsAsync(b, r.Data.EmployeeId, date, ct);
        var scope = await access.GetLocationReadScopeAsync(b, ct);
        if (scope.AllowedSources is not null && locations.Count == 0)
            throw new UnauthorizedAccessException("Employee has no authorized Primary location context.");
        foreach (var location in locations) await access.EnsureLocationAccessibleAsync(b, location, ct);
        access.RequireRealUserId();
        return await audit.ExecuteAsync(new RequiredAuditOperation(b, "hr.device_mapping.created", "HR",
            "Created an effective employee/device identity mapping.", "HrAttendanceDevice", r.DeviceId.ToString()),
            t => store.CreateMappingAsync(b, r.DeviceId, r.Data, scope, t), ct);
    }
    public async Task<HrDeviceMappingResponse> Handle(CloseHrDeviceMappingCommand r, CancellationToken ct)
    {
        if (r.Id <= 0 || r.ExpectedVersion <= 0 || r.EffectiveToUtc == default) throw new ArgumentException("Mapping ID, expected version and closure instant are required.");
        var b = access.RequireBusinessId(); await access.EnsureAsync(b, HrOrganisationOperation.ManageDeviceMappings, null, ct);
        var mapping = await store.MappingAsync(b, r.Id, ct);
        await DeviceScope(b, mapping.DeviceId, HrOrganisationOperation.ManageDeviceMappings, ct);
        access.RequireRealUserId();
        return await audit.ExecuteAsync(new RequiredAuditOperation(b, "hr.device_mapping.closed", "HR",
            "Closed a device mapping without deleting history.", "HrDeviceUserMapping", r.Id.ToString()),
            t => store.CloseMappingAsync(b, r.Id, r.ExpectedVersion, r.EffectiveToUtc, t), ct);
    }
    private async Task<HrDeviceResponse> DeviceScope(long b, long id, HrOrganisationOperation op, CancellationToken ct)
    {
        await access.EnsureAsync(b, op, null, ct);
        var device = await store.DeviceAsync(b, id, ct);
        await access.EnsureLocationAccessibleAsync(b, device.OperationalLocationProjectionId, ct); return device;
    }
    public static void Validate(HrDeviceData d)
    {
        ArgumentNullException.ThrowIfNull(d);
        Text(d.DeviceCode, 50, "Device code"); Text(d.SerialNumber, 150, "Serial"); Text(d.VendorCode, 50, "Vendor");
        Text(d.Model, 100, "Model"); Text(d.DeviceTimeZoneId, 100, "Timezone");
        if (d.OperationalLocationProjectionId <= 0 || d.ProtocolType is not ("ADMS" or "Push" or "API" or "File")
            || d.Status is not ("Active" or "Offline" or "Disabled" or "Error")
            || d.ConnectionPort is < 1 or > 65535
            || d.ConnectionPort.HasValue != !string.IsNullOrWhiteSpace(d.ConnectionHost)
            || d.FirmwareVersion?.Length > 100)
            throw new ArgumentException("Device location, protocol, status or connection metadata is invalid.");
        if (d.ConnectionHost is not null && (d.ConnectionHost.Length > 253
            || Uri.CheckHostName(d.ConnectionHost) == UriHostNameType.Unknown))
            throw new ArgumentException("Connection host must be a hostname/IP only, not a URI, path or credentials.");
        if (d.CredentialSecretReference is not null && !Regex.IsMatch(d.CredentialSecretReference, @"\A[a-zA-Z][a-zA-Z0-9_.-]{0,99}\z"))
            throw new ArgumentException("Credential reference must be an opaque external secret name, never a value/path.");
        try { TimeZoneInfo.FindSystemTimeZoneById(d.DeviceTimeZoneId); }
        catch (TimeZoneNotFoundException) { throw new ArgumentException("Unknown device timezone."); }
        catch (InvalidTimeZoneException) { throw new ArgumentException("Invalid device timezone."); }
    }
    private static void Text(string value, int limit, string field)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > limit || value.Any(char.IsControl))
            throw new ArgumentException($"{field} is required and must be at most {limit} characters without control characters.");
    }
}
