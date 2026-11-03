using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Core.Entities.HR.Approvals;

namespace RetailPOS.Application.HR.Approvals;

public sealed record CreateApprovalPolicyVersionCommand(
    string Code,
    ApprovalProcessType ProcessType,
    string Name,
    int VersionNo,
    ApprovalScopeType ScopeType,
    string? ScopeReference,
    bool AllowSelfApproval,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    bool IsActive,
    IReadOnlyList<ApprovalPolicyStepInput> Steps) : ICommand<long>;

public sealed record SubmitApprovalCommand(
    ApprovalProcessType ProcessType,
    string TargetType,
    string TargetId,
    ApprovalScopeType ScopeType,
    string? ScopeReference,
    long TargetVersion,
    string RequestHash,
    string? TargetPayload = null,
    string? IdempotencyKey = null) : ICommand<ApprovalDecisionResult>;

public sealed record RecordApprovalDecisionCommand(
    long InstanceId,
    string DecisionKey,
    ApprovalDecisionType Decision,
    string? Reason) : ICommand<ApprovalDecisionResult>;

public sealed record CancelApprovalCommand(
    long InstanceId,
    string DecisionKey,
    string Reason) : ICommand<ApprovalDecisionResult>;
