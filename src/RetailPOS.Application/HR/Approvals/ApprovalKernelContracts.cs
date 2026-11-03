using RetailPOS.Core.Entities.HR.Approvals;

namespace RetailPOS.Application.HR.Approvals;

public enum ApprovalKernelOperation
{
    ManagePolicy,
    Submit,
    Decide,
    Reject,
    Cancel
}

public sealed record ApprovalPolicyStepInput(
    int Sequence,
    ApprovalApproverType ApproverType,
    string? ApproverReference,
    int RequiredApprovals,
    bool CanReject,
    bool CanCancel);

public sealed record ApprovalTargetSnapshot(
    long Version,
    string Hash,
    IReadOnlyCollection<long>? DisallowedApproverUserIds = null,
    ApprovalDecisionResult? ReplayResult = null);

public sealed record ApprovalDecisionResult(
    long InstanceId,
    ApprovalInstanceStatus Status,
    int? CurrentStepSequence,
    bool IsReplay,
    string? TargetStatus = null);

public sealed class ApprovalKernelConflictException(string message) : InvalidOperationException(message);
public sealed class ApprovalKernelUnavailableException(string message) : InvalidOperationException(message);

public interface IApprovalKernelIdentity
{
    long RequireBusinessId();
    long RequireRealUserId();
    long? ActingRoleId { get; }
    Guid RequireCorrelationId();
}

public interface IApprovalKernelAccess
{
    Task EnsureAsync(
        long businessId,
        ApprovalProcessType processType,
        ApprovalKernelOperation operation,
        ApprovalScopeType scopeType,
        string? scopeReference,
        CancellationToken cancellationToken);
}

public interface IApprovalCandidateResolver
{
    Task<IReadOnlyCollection<long>> ResolveAsync(
        long businessId,
        ApprovalPolicy policy,
        ApprovalPolicyStep step,
        long requesterUserId,
        ApprovalScopeType scopeType,
        string? scopeReference,
        CancellationToken cancellationToken);
}

public interface IApprovalTargetSnapshotReader
{
    Task<ApprovalTargetSnapshot?> ReadAsync(
        long businessId,
        string targetType,
        string targetId,
        CancellationToken cancellationToken);
}

public interface IApprovalKernelWorkflow
{
    Task<ApprovalDecisionResult?> FindSubmissionReplayAsync(
        long businessId,
        SubmitApprovalCommand request,
        CancellationToken cancellationToken) =>
        Task.FromResult<ApprovalDecisionResult?>(null);
    Task<ApprovalTargetSnapshot?> PrepareSubmissionAsync(
        long businessId,
        long requesterUserId,
        SubmitApprovalCommand request,
        CancellationToken cancellationToken);
    Task SubmissionCreatedAsync(
        long businessId,
        ApprovalInstance instance,
        CancellationToken cancellationToken);
    Task DecisionCompletedAsync(
        long businessId,
        ApprovalInstance instance,
        long actorUserId,
        string? reason,
        CancellationToken cancellationToken);
}

public sealed class NoOpApprovalKernelWorkflow : IApprovalKernelWorkflow
{
    public Task<ApprovalTargetSnapshot?> PrepareSubmissionAsync(
        long businessId,
        long requesterUserId,
        SubmitApprovalCommand request,
        CancellationToken cancellationToken) =>
        Task.FromResult<ApprovalTargetSnapshot?>(null);

    public Task SubmissionCreatedAsync(
        long businessId,
        ApprovalInstance instance,
        CancellationToken cancellationToken) => Task.CompletedTask;

    public Task DecisionCompletedAsync(
        long businessId,
        ApprovalInstance instance,
        long actorUserId,
        string? reason,
        CancellationToken cancellationToken) => Task.CompletedTask;
}

public interface IApprovalKernelStore
{
    Task<int> GetLatestPolicyVersionAsync(long businessId, string code, CancellationToken cancellationToken);
    Task<ApprovalPolicy?> FindEffectivePolicyAsync(
        long businessId,
        ApprovalProcessType processType,
        ApprovalScopeType scopeType,
        string? scopeReference,
        DateOnly effectiveOn,
        CancellationToken cancellationToken);
    Task<ApprovalDecision?> FindDecisionByKeyAsync(
        long businessId,
        string decisionKey,
        CancellationToken cancellationToken);
    Task<ApprovalInstance?> FindInstanceForUpdateAsync(
        long businessId,
        long instanceId,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<ApprovalDecision>> GetStepDecisionsAsync(
        long businessId,
        long stepId,
        CancellationToken cancellationToken);
    void AddPolicy(ApprovalPolicy policy);
    void AddInstance(ApprovalInstance instance);
    Task PersistSubmissionAsync(CancellationToken cancellationToken);
    void AddDecision(ApprovalDecision decision);
}
