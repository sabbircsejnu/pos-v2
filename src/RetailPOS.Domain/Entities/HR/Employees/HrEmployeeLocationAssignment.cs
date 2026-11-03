using RetailPOS.Core.Entities.HR.Approvals;

namespace RetailPOS.Core.Entities.HR.Employees;

public sealed class HrEmployeeLocationAssignment
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long EmployeeId { get; set; }
    public long OperationalLocationProjectionId { get; set; }
    public string LocationNameSnapshot { get; set; } = string.Empty;
    public string SourceSystemSnapshot { get; set; } = string.Empty;
    public string SourceEntityTypeSnapshot { get; set; } = string.Empty;
    public string SourceEntityIdSnapshot { get; set; } = string.Empty;
    public long LocationSourceVersionSnapshot { get; set; }
    public string AssignmentType { get; set; } = "Primary";
    public bool IsTemporary { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public string Status { get; set; } = "Effective";
    public string Reason { get; set; } = string.Empty;
    public long RequestedByUserId { get; set; }
    public DateTime RequestedAtUtc { get; set; }
    public long? ApprovedByUserId { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public long? ApprovalInstanceId { get; set; }
    public ApprovalInstance? ApprovalInstance { get; set; }
    public long Version { get; set; } = 1;
}
