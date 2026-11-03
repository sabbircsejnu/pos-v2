namespace RetailPOS.Core.Entities.HR.Approvals;

public sealed class ApprovalDecision
{
    public long Id { get; set; }
    public long BusinessId { get; init; }
    public long ApprovalInstanceId { get; init; }
    public long ApprovalInstanceStepId { get; init; }
    public string DecisionKey { get; init; } = "";
    public string PayloadHash { get; init; } = "";
    public long ActorRealUserId { get; init; }
    public long? ActingRoleId { get; init; }
    public long? DelegatedForUserId { get; init; }
    public ApprovalDecisionType Decision { get; init; }
    public ApprovalInstanceStatus InstanceStatusAfterDecision { get; init; }
    public int? CurrentStepSequenceAfterDecision { get; init; }
    public string? Reason { get; init; }
    public string RequestHash { get; init; } = "";
    public DateTime OccurredAtUtc { get; init; }
    public Guid CorrelationId { get; init; }
}
