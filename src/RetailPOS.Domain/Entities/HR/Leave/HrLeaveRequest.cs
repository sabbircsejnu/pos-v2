using RetailPOS.Core.Entities.HR.Approvals;

namespace RetailPOS.Core.Entities.HR.Leave;

public sealed class HrLeaveRequest
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long EmployeeId { get; set; }
    public long LeaveTypeId { get; set; }
    public long ScopeLocationProjectionId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public decimal RequestedUnits { get; set; }
    public string Reason { get; set; } = "";
    public string Status { get; set; } = "Draft";
    public long RequestedByUserId { get; set; }
    public DateTime RequestedAtUtc { get; set; }
    public long? DecidedByUserId { get; set; }
    public DateTime? DecidedAtUtc { get; set; }
    public string? DecisionReason { get; set; }
    public long? ApprovalInstanceId { get; set; }
    public Guid? CurrentAttachmentVersionId { get; set; }
    public string? RequestHash { get; set; }
    public long? SubmittedTargetVersion { get; set; }
    public long Version { get; set; } = 1;
    public ApprovalInstance? ApprovalInstance { get; set; }
    public ICollection<HrLeaveRequestDay> Days { get; set; } = new List<HrLeaveRequestDay>();
}

public sealed class HrLeaveRequestDay
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long LeaveRequestId { get; set; }
    public long EmployeeId { get; set; }
    public DateOnly WorkDate { get; set; }
    public long? RosterId { get; set; }
    public long OperationalLocationProjectionId { get; set; }
    public long PolicyVersionId { get; set; }
    public decimal Units { get; set; }
    public string DayClassification { get; set; } = "";
    public string ShiftSnapshotJson { get; set; } = "";
    public string CalendarEvidenceJson { get; set; } = "";
}

public sealed class HrLeaveRequestAttachmentVersion
{
    public Guid Id { get; set; }
    public long BusinessId { get; set; }
    public long LeaveRequestId { get; set; }
    public int VersionNo { get; set; }
    public string? StorageKey { get; set; }
    public string OriginalFileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
    public string ContentHash { get; set; } = "";
    public string ScanStatus { get; set; } = "Pending";
    public string EncryptionKeyReference { get; set; } = "";
    public long UploadedByUserId { get; set; }
    public DateTime UploadedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
}
