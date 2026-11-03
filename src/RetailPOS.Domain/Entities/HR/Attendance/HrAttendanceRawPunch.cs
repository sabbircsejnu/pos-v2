namespace RetailPOS.Core.Entities.HR.Attendance;

public sealed class HrRawAttendancePunch
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long? DeviceId { get; set; }
    public string? DeviceSerialSnapshot { get; set; }
    public string? DeviceUserId { get; set; }
    public long? EmployeeId { get; set; }
    public DateTime PunchTimeLocal { get; set; }
    public DateTime? PunchTimeUtc { get; set; }
    public string? TimeZoneId { get; set; }
    public string VerificationMethod { get; set; } = "Unknown";
    public string? DirectionCode { get; set; }
    public DateTime ReceivedAtUtc { get; set; }
    public string? RawPayload { get; set; }
    public string IdempotencyFingerprint { get; set; } = "";
    public string SourceType { get; set; } = "Manual";
    public string ProcessingStatus { get; set; } = "Pending";
    public string? ProcessingError { get; set; }
    public int ProcessingVersion { get; set; }
}
