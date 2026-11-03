using RetailPOS.Core.Entities.HR.Approvals;

namespace RetailPOS.Core.Entities.HR.Attendance;

public sealed class HrAttendanceCorrectionRequest
{
    public Guid Id { get; set; }
    public long BusinessId { get; set; }
    public long DailyAttendanceId { get; set; }
    public long EmployeeId { get; set; }
    public DateOnly WorkDate { get; set; }
    public long OperationalLocationProjectionId { get; set; }
    public int CorrectionSequence { get; set; }
    public int ExpectedProcessingVersion { get; set; }
    public string RequestHash { get; set; } = "";
    public string Reason { get; set; } = "";
    public string BeforeEvidenceJson { get; set; } = "";
    public string ProposedEvidenceJson { get; set; } = "";
    public string? AppliedAfterEvidenceJson { get; set; }
    public string Status { get; set; } = "PendingApproval";
    public long RequestedByUserId { get; set; }
    public DateTime RequestedAtUtc { get; set; }
    public long? DecidedByUserId { get; set; }
    public DateTime? DecidedAtUtc { get; set; }
    public string? DecisionReason { get; set; }
    public long ApprovalInstanceId { get; set; }
    public ApprovalInstance ApprovalInstance { get; set; } = null!;
    public int Version { get; set; } = 1;
}
