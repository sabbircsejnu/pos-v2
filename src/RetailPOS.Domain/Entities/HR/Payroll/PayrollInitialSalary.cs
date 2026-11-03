using RetailPOS.Core.Entities.HR.Approvals;

namespace RetailPOS.Core.Entities.HR.Payroll;

public sealed class PayrollSalarySetupRequest
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long EmployeeId { get; set; }
    public long SalaryStructureId { get; set; }
    public long SalaryStructureVersion { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public long OperationalLocationProjectionId { get; set; }
    public string StructureSnapshotJson { get; set; } = "";
    public string ComponentSnapshotJson { get; set; } = "";
    public string RequestHash { get; set; } = "";
    public string IdempotencyPayloadHash { get; set; } = "";
    public string IdempotencyKey { get; set; } = "";
    public string Reason { get; set; } = "";
    public string Status { get; set; } = "Draft";
    public long? ApprovalInstanceId { get; set; }
    public ApprovalInstance? ApprovalInstance { get; set; }
    public long CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public long? SubmittedByUserId { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }
    public long? SubmittedTargetVersion { get; set; }
    public long? DecidedByUserId { get; set; }
    public DateTime? DecidedAtUtc { get; set; }
    public string? DecisionReason { get; set; }
    public long Version { get; set; } = 1;
}

public sealed class PayrollEmployeeSalaryVersion
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long EmployeeId { get; set; }
    public long SalarySetupRequestId { get; set; }
    public long? SalaryRevisionRequestId { get; set; }
    public long? PredecessorSalaryVersionId { get; set; }
    public long SalaryStructureId { get; set; }
    public long SalaryStructureVersion { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public long OperationalLocationProjectionId { get; set; }
    public string StructureSnapshotJson { get; set; } = "";
    public string ComponentSnapshotJson { get; set; } = "";
    public long ApprovalInstanceId { get; set; }
    public long ApprovedByUserId { get; set; }
    public DateTime ApprovedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class PayrollSalaryRevisionRequest
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long EmployeeId { get; set; }
    public long SourceSalaryVersionId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public long OperationalLocationProjectionId { get; set; }
    public string StructureSnapshotJson { get; set; } = "";
    public string CurrentComponentSnapshotJson { get; set; } = "";
    public string ProposedComponentSnapshotJson { get; set; } = "";
    public string RequestHash { get; set; } = "";
    public string IdempotencyPayloadHash { get; set; } = "";
    public string IdempotencyKey { get; set; } = "";
    public string Reason { get; set; } = "";
    public string Status { get; set; } = "Draft";
    public long? ApprovalInstanceId { get; set; }
    public ApprovalInstance? ApprovalInstance { get; set; }
    public long CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public long? SubmittedByUserId { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }
    public long? SubmittedTargetVersion { get; set; }
    public long? DecidedByUserId { get; set; }
    public DateTime? DecidedAtUtc { get; set; }
    public string? DecisionReason { get; set; }
    public long Version { get; set; } = 1;
}

public sealed class PayrollEmployeeAdjustmentVersion
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long EmployeeId { get; set; }
    public Guid AdjustmentIdentity { get; set; }
    public int Version { get; set; }
    public long? PredecessorVersionId { get; set; }
    public long SalaryComponentId { get; set; }
    public string ComponentCodeSnapshot { get; set; } = "";
    public string ComponentNameSnapshot { get; set; } = "";
    public string ComponentTypeSnapshot { get; set; } = "";
    public string ValueTypeSnapshot { get; set; } = "";
    public string AdjustmentKind { get; set; } = "";
    public string Classification { get; set; } = "";
    public decimal Amount { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public long OperationalLocationProjectionId { get; set; }
    public string Reason { get; set; } = "";
    public string? Reference { get; set; }
    public string Status { get; set; } = "Active";
    public string IdempotencyKey { get; set; } = "";
    public string IdempotencyPayloadHash { get; set; } = "";
    public long CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
