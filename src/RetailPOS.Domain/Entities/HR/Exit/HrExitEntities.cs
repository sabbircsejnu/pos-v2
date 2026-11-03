namespace RetailPOS.Core.Entities.HR.Exit;

using RetailPOS.Core.Entities.HR.Approvals;
using RetailPOS.Core.Entities.HR.Employees;

public enum HrClearanceTemplateScopeType
{
    Business,
    LocationType,
    Location
}

public sealed class HrExitCase
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long EmployeeId { get; set; }
    public string ExitType { get; set; } = string.Empty;
    public DateOnly InitiatedDate { get; set; }
    public DateOnly ProposedLastWorkingDate { get; set; }
    public DateOnly? ActualLastWorkingDate { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string Status { get; set; } = "Draft";
    public long? ApprovalInstanceId { get; set; }
    public long InitiatedByUserId { get; set; }
    public DateTime InitiatedAtUtc { get; set; }
    public long? PrimaryLocationProjectionIdSnapshot { get; set; }
    public string? LocationTypeSnapshot { get; set; }
    public long ClearanceTemplateId { get; set; }
    public int ClearanceTemplateVersionNo { get; set; }
    public string ClearanceTemplateSnapshotJson { get; set; } = string.Empty;
    public long Version { get; set; } = 1;
    public HrEmployee Employee { get; set; } = null!;
    public ApprovalInstance? ApprovalInstance { get; set; }
    public HrExitCompletionEvidence? CompletionEvidence { get; set; }
    public List<HrClearanceItem> ClearanceItems { get; set; } = [];
    public List<HrExitCaseEvent> Events { get; set; } = [];
}

public sealed class HrExitCompletionEvidence
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long ExitCaseId { get; set; }
    public long ExitVersion { get; set; }
    public bool PayrollEnabled { get; set; }
    public string CompletionMode { get; set; } = "HrOnly";
    public long? FinalSettlementId { get; set; }
    public long? FinalSettlementRevisionId { get; set; }
    public int? FinalSettlementRevisionNo { get; set; }
    public string? FinalSettlementStatus { get; set; }
    public string? FinalSettlementEvidenceHash { get; set; }
    public decimal? NetSettlement { get; set; }
    public string? CurrencyCode { get; set; }
    public string EvidenceJson { get; set; } = "{}";
    public string EvidenceHash { get; set; } = "";
    public long CompletedByUserId { get; set; }
    public DateTime CompletedAtUtc { get; set; }
    public HrExitCase ExitCase { get; set; } = null!;
}

public sealed class HrClearanceTemplate
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public HrClearanceTemplateScopeType ScopeType { get; set; }
    public string? ScopeReference { get; set; }
    public int VersionNo { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsActive { get; set; }
    public long CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public List<HrClearanceTemplateItem> Items { get; set; } = [];
}

public sealed class HrClearanceTemplateItem
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long TemplateId { get; set; }
    public int Sequence { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ResponsibleRole { get; set; }
    public string? ResponsibleReference { get; set; }
    public bool IsMandatory { get; set; }
}

public sealed class HrClearanceItem
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long ExitCaseId { get; set; }
    public long? TemplateItemId { get; set; }
    public int Sequence { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ResponsibleRole { get; set; }
    public string? ResponsibleReference { get; set; }
    public bool IsMandatory { get; set; }
    public string Status { get; set; } = "Pending";
    public long? ClearedByUserId { get; set; }
    public DateTime? ClearedAtUtc { get; set; }
    public string? Comment { get; set; }
    public string? EvidenceReference { get; set; }
    public long Version { get; set; } = 1;
    public HrExitCase ExitCase { get; set; } = null!;
    public HrClearanceTemplateItem? TemplateItem { get; set; }
}

public sealed class HrExitCaseEvent
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long ExitCaseId { get; set; }
    public int Sequence { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string? PreviousStatus { get; set; }
    public string NewStatus { get; set; } = string.Empty;
    public DateOnly? ActualLastWorkingDate { get; set; }
    public string? Reason { get; set; }
    public long ActorUserId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
}
