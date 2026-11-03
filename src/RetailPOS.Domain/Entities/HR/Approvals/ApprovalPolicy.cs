namespace RetailPOS.Core.Entities.HR.Approvals;

public sealed class ApprovalPolicy
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public string Code { get; set; } = "";
    public ApprovalProcessType ProcessType { get; set; }
    public string Name { get; set; } = "";
    public int VersionNo { get; set; }
    public ApprovalScopeType ScopeType { get; set; }
    public string? ScopeReference { get; set; }
    public bool AllowSelfApproval { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsActive { get; set; }
    public ICollection<ApprovalPolicyStep> Steps { get; set; } = new List<ApprovalPolicyStep>();
}

public sealed class ApprovalPolicyStep
{
    public long Id { get; set; }
    public long ApprovalPolicyId { get; set; }
    public int Sequence { get; set; }
    public ApprovalApproverType ApproverType { get; set; }
    public string? ApproverReference { get; set; }
    public int RequiredApprovals { get; set; }
    public bool CanReject { get; set; }
    public bool CanCancel { get; set; }
    public ApprovalPolicy Policy { get; set; } = null!;
}
