namespace RetailPOS.Core.Entities.HR.Attendance;

public sealed class HrAttendanceDevice
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public string DeviceCode { get; set; } = "";
    public string SerialNumber { get; set; } = "";
    public string VendorCode { get; set; } = "";
    public string Model { get; set; } = "";
    public long OperationalLocationProjectionId { get; set; }
    public string ProtocolType { get; set; } = "";
    public string? ConnectionHost { get; set; }
    public int? ConnectionPort { get; set; }
    public string? CredentialSecretReference { get; set; }
    public string? FirmwareVersion { get; set; }
    public DateTime? LastSeenAtUtc { get; set; }
    public string DeviceTimeZoneId { get; set; } = "";
    public string Status { get; set; } = "Active";
    public long Version { get; set; } = 1;
}

public sealed class HrDeviceUserMapping
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long DeviceId { get; set; }
    public long EmployeeId { get; set; }
    public string DeviceUserId { get; set; } = "";
    public DateTime EffectiveFromUtc { get; set; }
    public DateTime? EffectiveToUtc { get; set; }
    public bool IsActive { get; set; } = true;
    public long Version { get; set; } = 1;
}
