using RetailPOS.Core.Entities.HR.Approvals;

namespace RetailPOS.Core.Entities.HR.Employees;

public sealed class HrEmployeeLocationTransferRequest
{
    public Guid Id { get; set; }
    public long BusinessId { get; set; }
    public long EmployeeId { get; set; }
    public long TargetLocationProjectionId { get; set; }
    public string LocationNameSnapshot { get; set; } = string.Empty;
    public string SourceSystemSnapshot { get; set; } = string.Empty;
    public string SourceEntityTypeSnapshot { get; set; } = string.Empty;
    public string SourceEntityIdSnapshot { get; set; } = string.Empty;
    public long LocationSourceVersionSnapshot { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public long ExpectedEmployeeVersion { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public string Status { get; set; } = "PendingApproval";
    public long? ApprovalInstanceId { get; set; }
    public ApprovalInstance? ApprovalInstance { get; set; }
    public long RequestedByUserId { get; set; }
    public DateTime RequestedAtUtc { get; set; }
    public long? DecidedByUserId { get; set; }
    public DateTime? DecidedAtUtc { get; set; }
    public string? DecisionReason { get; set; }
    public long Version { get; set; } = 1;
}
