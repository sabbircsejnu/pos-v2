namespace RetailPOS.Core.Entities.HR.Approvals;

public sealed class ApprovalInstance
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long PolicyId { get; set; }
    public string PolicyCodeSnapshot { get; set; } = "";
    public int PolicyVersionSnapshot { get; set; }
    public bool AllowSelfApprovalSnapshot { get; set; }
    public ApprovalProcessType ProcessType { get; set; }
    public string TargetType { get; set; } = "";
    public string TargetId { get; set; } = "";
    public long RequestedByUserId { get; set; }
    public DateTime RequestedAtUtc { get; set; }
    public ApprovalInstanceStatus Status { get; set; }
    public int? CurrentStepSequence { get; set; }
    public Guid CorrelationId { get; set; }
    public ApprovalScopeType ScopeType { get; set; }
    public string? ScopeReference { get; set; }
    public long TargetVersion { get; set; }
    public string RequestHash { get; set; } = "";
    public uint XMin { get; set; }
    public ICollection<ApprovalInstanceStep> Steps { get; set; } = new List<ApprovalInstanceStep>();
}

public sealed class ApprovalInstanceStep
{
    public long Id { get; set; }
    public long ApprovalInstanceId { get; set; }
    public int Sequence { get; set; }
    public ApprovalApproverType ResolvedApproverType { get; set; }
    public string ResolvedApproverReference { get; set; } = "";
    public string ResolvedApproverCandidatesJson { get; set; } = "[]";
    public ApprovalStepDecision Decision { get; set; }
    public long? DecisionByUserId { get; set; }
    public DateTime? DecisionAtUtc { get; set; }
    public string? Comment { get; set; }
    public int RequiredApprovalsSnapshot { get; set; }
    public int PolicyStepVersionSnapshot { get; set; }
    public bool CanRejectSnapshot { get; set; }
    public bool CanCancelSnapshot { get; set; }
    public ApprovalInstance Instance { get; set; } = null!;
}
