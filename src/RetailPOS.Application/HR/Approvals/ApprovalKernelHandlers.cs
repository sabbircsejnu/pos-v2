using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediatR;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.Authorization;
using RetailPOS.Core.Entities.HR.Approvals;

namespace RetailPOS.Application.HR.Approvals;

public sealed class ApprovalKernelHandlers(
    IApprovalKernelStore store,
    IApprovalKernelIdentity identity,
    IApprovalKernelAccess access,
    IApprovalCandidateResolver candidates,
    IApprovalTargetSnapshotReader targets,
    IApprovalKernelWorkflow workflow,
    IRequiredAuditTransaction audit)
    : IRequestHandler<CreateApprovalPolicyVersionCommand, long>,
      IRequestHandler<SubmitApprovalCommand, ApprovalDecisionResult>,
      IRequestHandler<RecordApprovalDecisionCommand, ApprovalDecisionResult>,
      IRequestHandler<CancelApprovalCommand, ApprovalDecisionResult>
{
    private const string AuditModule = "HR";

    public async Task<long> Handle(CreateApprovalPolicyVersionCommand request, CancellationToken cancellationToken)
    {
        ValidatePolicy(request);
        var businessId = RequireBusiness();
        await access.EnsureAsync(businessId, request.ProcessType, ApprovalKernelOperation.ManagePolicy,
            request.ScopeType, request.ScopeReference, cancellationToken);

        var previousVersion = await store.GetLatestPolicyVersionAsync(businessId, request.Code, cancellationToken);
        if (request.VersionNo != previousVersion + 1)
            throw new ApprovalKernelConflictException("Approval policy versions must be created sequentially.");

        var policy = new ApprovalPolicy
        {
            BusinessId = businessId,
            Code = request.Code.Trim(),
            ProcessType = request.ProcessType,
            Name = request.Name.Trim(),
            VersionNo = request.VersionNo,
            ScopeType = request.ScopeType,
            ScopeReference = NormalizeScopeReference(request.ScopeType, request.ScopeReference),
            AllowSelfApproval = request.AllowSelfApproval,
            EffectiveFrom = request.EffectiveFrom,
            EffectiveTo = request.EffectiveTo,
            IsActive = request.IsActive,
            Steps = request.Steps.OrderBy(step => step.Sequence).Select(step => new ApprovalPolicyStep
            {
                Sequence = step.Sequence,
                ApproverType = step.ApproverType,
                ApproverReference = string.IsNullOrWhiteSpace(step.ApproverReference) ? null : step.ApproverReference.Trim(),
                RequiredApprovals = step.RequiredApprovals,
                CanReject = step.CanReject,
                CanCancel = step.CanCancel
            }).ToList()
        };

        var operation = new RequiredAuditOperation(businessId, "hr.approval_policy.version_created",
            AuditModule, $"Created approval policy {policy.Code} version {policy.VersionNo}.");
        var created = await audit.ExecuteAsync(operation, _ =>
        {
            store.AddPolicy(policy);
            return Task.FromResult(policy);
        }, _ => true, cancellationToken);
        return created.Id;
    }

    public async Task<ApprovalDecisionResult> Handle(SubmitApprovalCommand request, CancellationToken cancellationToken)
    {
        ValidateTarget(request.TargetType, request.TargetId, request.TargetVersion, request.RequestHash);
        if (request.ProcessType == ApprovalProcessType.FinalSettlement
            && (string.IsNullOrWhiteSpace(request.IdempotencyKey)
                || request.IdempotencyKey.Trim().Length > 150))
            throw new ArgumentException(
                "A valid final settlement approval idempotency key is required.",
                nameof(request.IdempotencyKey));
        var businessId = RequireBusiness();
        var requesterId = identity.RequireRealUserId();
        if (requesterId <= 0)
            throw new UnauthorizedAccessException("A real authenticated user is required to submit approval.");
        await access.EnsureAsync(businessId, request.ProcessType, ApprovalKernelOperation.Submit,
            request.ScopeType, request.ScopeReference, cancellationToken);
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var replay = await workflow.FindSubmissionReplayAsync(businessId, request, cancellationToken);
            if (replay is not null)
                return replay with { IsReplay = true };
        }

        var operation = request.ProcessType == ApprovalProcessType.FinalSettlement
            ? new RequiredAuditOperation(businessId, "payroll.final_settlement.requested",
                "Payroll", "Requested approval for a frozen Payroll final settlement.",
                request.TargetType, request.TargetId)
            : new RequiredAuditOperation(businessId, "hr.approval.submitted",
                AuditModule, $"Submitted {request.ProcessType} approval.");
        var created = await audit.ExecuteAsync(operation, async token =>
        {
            var target = await workflow.PrepareSubmissionAsync(businessId, requesterId, request, token)
                ?? await targets.ReadAsync(businessId, request.TargetType.Trim(), request.TargetId.Trim(), token)
                ?? throw new KeyNotFoundException("Approval target was not found.");
            EnsureTargetMatches(target, request.TargetVersion, request.RequestHash);
            if (target.ReplayResult is { } replay)
                return replay with { IsReplay = true };

            var policy = await store.FindEffectivePolicyAsync(
                businessId, request.ProcessType, request.ScopeType,
                NormalizeScopeReference(request.ScopeType, request.ScopeReference),
                DateOnly.FromDateTime(DateTime.UtcNow), token);
            if (policy is null
                && request.ProcessType is (ApprovalProcessType.SalarySetup
                    or ApprovalProcessType.SalaryRevision or ApprovalProcessType.EmployeeLoan
                    or ApprovalProcessType.PayrollRun or ApprovalProcessType.FinalSettlement)
                && request.ScopeType == ApprovalScopeType.Location)
                policy = await store.FindEffectivePolicyAsync(
                    businessId, request.ProcessType, ApprovalScopeType.Business, null,
                    DateOnly.FromDateTime(DateTime.UtcNow), token);
            if (policy is null || policy.Steps.Count == 0)
                throw new ApprovalKernelUnavailableException("No effective approval policy is configured for this Business and scope.");

            var instance = new ApprovalInstance
            {
                BusinessId = businessId,
                PolicyId = policy.Id,
                PolicyCodeSnapshot = policy.Code,
                PolicyVersionSnapshot = policy.VersionNo,
                AllowSelfApprovalSnapshot = policy.AllowSelfApproval
                    && request.ProcessType is not (ApprovalProcessType.EmployeeLoan
                        or ApprovalProcessType.PayrollRun or ApprovalProcessType.FinalSettlement),
                ProcessType = policy.ProcessType,
                TargetType = request.TargetType.Trim(),
                TargetId = request.TargetId.Trim(),
                RequestedByUserId = requesterId,
                RequestedAtUtc = DateTime.UtcNow,
                Status = ApprovalInstanceStatus.Pending,
                CurrentStepSequence = policy.Steps.Min(step => step.Sequence),
                CorrelationId = identity.RequireCorrelationId(),
                ScopeType = request.ScopeType,
                ScopeReference = NormalizeScopeReference(request.ScopeType, request.ScopeReference),
                TargetVersion = request.TargetVersion,
                RequestHash = NormalizeHash(request.RequestHash)
            };

            foreach (var policyStep in policy.Steps.OrderBy(step => step.Sequence))
            {
                var resolved = await candidates.ResolveAsync(
                    businessId, policy, policyStep, requesterId,
                    request.ScopeType, instance.ScopeReference, token);
                var frozenCandidates = resolved.Where(userId => userId > 0).Distinct().Order().ToArray();
                if (!policy.AllowSelfApproval
                    || request.ProcessType is ApprovalProcessType.EmployeeLoan
                        or ApprovalProcessType.PayrollRun or ApprovalProcessType.FinalSettlement)
                    frozenCandidates = frozenCandidates.Where(userId => userId != requesterId).ToArray();
                if (target.DisallowedApproverUserIds is not null)
                    frozenCandidates = frozenCandidates
                        .Where(userId => !target.DisallowedApproverUserIds.Contains(userId)).ToArray();
                if (frozenCandidates.Length < policyStep.RequiredApprovals)
                    throw new ApprovalKernelUnavailableException(
                        $"Approval step {policyStep.Sequence} has fewer valid approvers than its required quorum.");

                instance.Steps.Add(new ApprovalInstanceStep
                {
                    Sequence = policyStep.Sequence,
                    ResolvedApproverType = policyStep.ApproverType,
                    ResolvedApproverReference = policyStep.ApproverReference ?? "",
                    ResolvedApproverCandidatesJson = JsonSerializer.Serialize(frozenCandidates),
                    Decision = ApprovalStepDecision.Pending,
                    RequiredApprovalsSnapshot = policyStep.RequiredApprovals,
                    PolicyStepVersionSnapshot = policy.VersionNo,
                    CanRejectSnapshot = policyStep.CanReject,
                    CanCancelSnapshot = policyStep.CanCancel
                });
            }

            store.AddInstance(instance);
            await store.PersistSubmissionAsync(token);
            await workflow.SubmissionCreatedAsync(businessId, instance, token);
            return new ApprovalDecisionResult(instance.Id, instance.Status,
                instance.CurrentStepSequence, false, instance.Status.ToString());
        }, result => !result.IsReplay, cancellationToken);
        return created;
    }

    public async Task<ApprovalDecisionResult> Handle(RecordApprovalDecisionCommand request, CancellationToken cancellationToken)
    {
        ValidateDecision(request.DecisionKey, request.Decision, request.Reason);
        if (request.Decision == ApprovalDecisionType.Cancel)
            throw new ArgumentException("Use the cancellation command to cancel an approval instance.", nameof(request));

        var businessId = RequireBusiness();
        var actorId = identity.RequireRealUserId();
        var operation = new RequiredAuditOperation(businessId, "hr.approval.decided",
            AuditModule, $"Recorded an {request.Decision.ToString().ToLowerInvariant()} approval decision.",
            "ApprovalInstance", request.InstanceId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var payloadHash = HashDecision(request.DecisionKey, request.Decision, request.Reason, actorId);

        return await audit.ExecuteAsync(operation, async token =>
        {
            var instance = await store.FindInstanceForUpdateAsync(businessId, request.InstanceId, token)
                ?? throw new UnauthorizedAccessException("Approval instance was not found in the current Business.");
            await access.EnsureAsync(businessId, instance.ProcessType,
                request.Decision == ApprovalDecisionType.Reject
                    ? ApprovalKernelOperation.Reject
                    : ApprovalKernelOperation.Decide,
                instance.ScopeType, instance.ScopeReference, token);
            var prior = await store.FindDecisionByKeyAsync(businessId, request.DecisionKey.Trim(), token);
            if (prior is not null)
            {
                if (prior.ApprovalInstanceId != request.InstanceId
                    || !CryptographicOperations.FixedTimeEquals(
                        Convert.FromHexString(prior.PayloadHash), Convert.FromHexString(payloadHash)))
                    throw new ApprovalKernelConflictException("The decision key was already used with a different payload.");
                return new ApprovalDecisionResult(
                    prior.ApprovalInstanceId, prior.InstanceStatusAfterDecision,
                    prior.CurrentStepSequenceAfterDecision, true);
            }

            EnsurePending(instance);
            await EnsureTargetCurrentAsync(instance, token);
            var step = GetCurrentStep(instance);
            EnsureCandidate(step, actorId, instance.RequestedByUserId,
                instance.ProcessType is ApprovalProcessType.SalarySetup
                    or ApprovalProcessType.SalaryRevision or ApprovalProcessType.EmployeeLoan
                    or ApprovalProcessType.FinalSettlement
                    ? false
                    : instance.AllowSelfApprovalSnapshot);
            if (request.Decision == ApprovalDecisionType.Reject && !step.CanRejectSnapshot)
                throw new UnauthorizedAccessException("This approval step does not permit rejection.");

            var existingStepDecisions = await store.GetStepDecisionsAsync(businessId, step.Id, token);
            if (existingStepDecisions.Any(item => item.ActorRealUserId == actorId))
                throw new ApprovalKernelConflictException("A real user can satisfy a quorum only once per step.");

            var now = DateTime.UtcNow;
            var nextStatus = instance.Status;
            if (request.Decision == ApprovalDecisionType.Reject)
            {
                step.Decision = ApprovalStepDecision.Rejected;
                step.DecisionByUserId = actorId;
                step.DecisionAtUtc = now;
                step.Comment = request.Reason?.Trim();
                instance.Status = ApprovalInstanceStatus.Rejected;
                instance.CurrentStepSequence = null;
                MarkOtherPendingSteps(instance, ApprovalStepDecision.Skipped);
                nextStatus = instance.Status;
            }
            else
            {
                step.DecisionByUserId = actorId;
                step.DecisionAtUtc = now;
                step.Comment = request.Reason?.Trim();
                var approvals = existingStepDecisions.Count(item => item.Decision == ApprovalDecisionType.Approve) + 1;
                if (approvals >= step.RequiredApprovalsSnapshot)
                {
                    step.Decision = ApprovalStepDecision.Approved;
                    var nextStep = instance.Steps.Where(item => item.Sequence > step.Sequence)
                        .OrderBy(item => item.Sequence).FirstOrDefault();
                    if (nextStep is null)
                    {
                        instance.Status = ApprovalInstanceStatus.Approved;
                        instance.CurrentStepSequence = null;
                    }
                    else
                    {
                        instance.CurrentStepSequence = nextStep.Sequence;
                    }
                }
                nextStatus = instance.Status;
            }

            store.AddDecision(new ApprovalDecision
            {
                BusinessId = businessId,
                ApprovalInstanceId = instance.Id,
                ApprovalInstanceStepId = step.Id,
                DecisionKey = request.DecisionKey.Trim(),
                PayloadHash = payloadHash,
                ActorRealUserId = actorId,
                ActingRoleId = identity.ActingRoleId,
                Decision = request.Decision,
                InstanceStatusAfterDecision = nextStatus,
                CurrentStepSequenceAfterDecision = instance.CurrentStepSequence,
                Reason = request.Reason?.Trim(),
                RequestHash = instance.RequestHash,
                OccurredAtUtc = now,
                CorrelationId = identity.RequireCorrelationId()
            });
            if (instance.Status != ApprovalInstanceStatus.Pending)
                await workflow.DecisionCompletedAsync(
                    businessId, instance, actorId, request.Reason, token);
            return new ApprovalDecisionResult(instance.Id, nextStatus, instance.CurrentStepSequence, false);
        }, result => !result.IsReplay, cancellationToken);
    }

    public async Task<ApprovalDecisionResult> Handle(CancelApprovalCommand request, CancellationToken cancellationToken)
    {
        ValidateDecision(request.DecisionKey, ApprovalDecisionType.Cancel, request.Reason);
        var businessId = RequireBusiness();
        var actorId = identity.RequireRealUserId();
        var payloadHash = HashDecision(request.DecisionKey, ApprovalDecisionType.Cancel, request.Reason, actorId);
        var operation = new RequiredAuditOperation(businessId, "hr.approval.cancelled",
            AuditModule, "Cancelled a pending approval instance.",
            "ApprovalInstance", request.InstanceId.ToString(System.Globalization.CultureInfo.InvariantCulture));

        return await audit.ExecuteAsync(operation, async token =>
        {
            var instance = await store.FindInstanceForUpdateAsync(businessId, request.InstanceId, token)
                ?? throw new UnauthorizedAccessException("Approval instance was not found in the current Business.");
            await access.EnsureAsync(businessId, instance.ProcessType, ApprovalKernelOperation.Cancel,
                instance.ScopeType, instance.ScopeReference, token);
            var prior = await store.FindDecisionByKeyAsync(businessId, request.DecisionKey.Trim(), token);
            if (prior is not null)
            {
                if (prior.ApprovalInstanceId != request.InstanceId
                    || !CryptographicOperations.FixedTimeEquals(
                        Convert.FromHexString(prior.PayloadHash), Convert.FromHexString(payloadHash)))
                    throw new ApprovalKernelConflictException("The decision key was already used with a different payload.");
                return new ApprovalDecisionResult(
                    prior.ApprovalInstanceId, prior.InstanceStatusAfterDecision,
                    prior.CurrentStepSequenceAfterDecision, true);
            }

            EnsurePending(instance);
            await EnsureTargetCurrentAsync(instance, token);
            var step = GetCurrentStep(instance);
            var isRequester = instance.RequestedByUserId == actorId;
            var isPermittedStepApprover = DeserializeCandidates(step).Contains(actorId)
                && step.CanCancelSnapshot;
            if (!isRequester && !isPermittedStepApprover)
                throw new UnauthorizedAccessException("Only the requester or an explicitly eligible step approver may cancel.");

            step.Decision = ApprovalStepDecision.Cancelled;
            step.DecisionByUserId = actorId;
            step.DecisionAtUtc = DateTime.UtcNow;
            step.Comment = request.Reason.Trim();
            MarkOtherPendingSteps(instance, ApprovalStepDecision.Cancelled);
            instance.Status = ApprovalInstanceStatus.Cancelled;
            instance.CurrentStepSequence = null;
            var now = DateTime.UtcNow;
            store.AddDecision(new ApprovalDecision
            {
                BusinessId = businessId,
                ApprovalInstanceId = instance.Id,
                ApprovalInstanceStepId = step.Id,
                DecisionKey = request.DecisionKey.Trim(),
                PayloadHash = payloadHash,
                ActorRealUserId = actorId,
                ActingRoleId = identity.ActingRoleId,
                Decision = ApprovalDecisionType.Cancel,
                InstanceStatusAfterDecision = instance.Status,
                CurrentStepSequenceAfterDecision = null,
                Reason = request.Reason.Trim(),
                RequestHash = instance.RequestHash,
                OccurredAtUtc = now,
                CorrelationId = identity.RequireCorrelationId()
            });
            await workflow.DecisionCompletedAsync(
                businessId, instance, actorId, request.Reason, token);
            return new ApprovalDecisionResult(instance.Id, instance.Status, null, false);
        }, result => !result.IsReplay, cancellationToken);
    }

    private long RequireBusiness()
    {
        var businessId = identity.RequireBusinessId();
        if (businessId <= 0)
            throw new BusinessContextRequiredException("A validated Business scope is required.");
        return businessId;
    }

    private static void ValidatePolicy(CreateApprovalPolicyVersionCommand request)
    {
        if (!Enum.IsDefined(request.ProcessType))
            throw new ArgumentOutOfRangeException(nameof(request.ProcessType));
        if (request.ProcessType is (ApprovalProcessType.SalarySetup
            or ApprovalProcessType.SalaryRevision or ApprovalProcessType.EmployeeLoan
            or ApprovalProcessType.PayrollRun or ApprovalProcessType.FinalSettlement)
            && request.AllowSelfApproval)
            throw new ArgumentException(
                "Salary setup, revision, employee loan, and Payroll Run policies cannot permit self-approval.");
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Code);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);
        if (request.Code.Trim().Length > 100 || request.Name.Trim().Length > 150 || request.VersionNo <= 0)
            throw new ArgumentException("Approval policy code, name or version is invalid.");
        ValidateScope(request.ScopeType, request.ScopeReference);
        if (request.EffectiveTo < request.EffectiveFrom)
            throw new ArgumentException("Approval policy effective-to date precedes effective-from date.");
        if (request.Steps is null || request.Steps.Count == 0)
            throw new ArgumentException("An approval policy requires at least one step.");
        if (request.ProcessType == ApprovalProcessType.PayrollRun && request.Steps.Count != 1)
            throw new ArgumentException("Payroll Run approval policies must have exactly one approval step.");
        var ordered = request.Steps.OrderBy(step => step.Sequence).ToArray();
        if (ordered.Select(step => step.Sequence).Where((sequence, index) => sequence != index + 1).Any())
            throw new ArgumentException("Approval policy step sequences must be contiguous starting at one.");
        if (ordered.Any(step => step.RequiredApprovals <= 0
            || (step.ApproverType is ApprovalApproverType.User or ApprovalApproverType.Role)
            && string.IsNullOrWhiteSpace(step.ApproverReference)))
            throw new ArgumentException("Each policy step requires a positive quorum and a valid resolver reference.");
    }

    private static void ValidateTarget(string targetType, string targetId, long targetVersion, string hash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetType);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId);
        if (targetType.Trim().Length > 100 || targetId.Trim().Length > 150 || targetVersion < 0)
            throw new ArgumentException("Approval target identity or version is invalid.");
        _ = NormalizeHash(hash);
    }

    private static void ValidateDecision(string key, ApprovalDecisionType decision, string? reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (key.Trim().Length > 150)
            throw new ArgumentException("Decision key exceeds its maximum length.", nameof(key));
        if (decision is not (ApprovalDecisionType.Approve or ApprovalDecisionType.Reject or ApprovalDecisionType.Cancel))
            throw new ArgumentOutOfRangeException(nameof(decision));
        if (decision is ApprovalDecisionType.Reject or ApprovalDecisionType.Cancel
            && string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A reason is required for rejection and cancellation.", nameof(reason));
        if (reason?.Length > 1000)
            throw new ArgumentException("Decision reason exceeds its maximum length.", nameof(reason));
    }

    private static void ValidateScope(ApprovalScopeType type, string? reference)
    {
        if (!Enum.IsDefined(type))
            throw new ArgumentOutOfRangeException(nameof(type));
        if (type == ApprovalScopeType.Business && !string.IsNullOrWhiteSpace(reference)
            || type != ApprovalScopeType.Business && string.IsNullOrWhiteSpace(reference))
            throw new ArgumentException("Approval scope reference does not match its scope type.");
    }

    private static string? NormalizeScopeReference(ApprovalScopeType type, string? reference) =>
        type == ApprovalScopeType.Business ? null : reference?.Trim();

    private static string NormalizeHash(string hash)
    {
        if (hash is null || hash.Length != 64 || !hash.All(Uri.IsHexDigit))
            throw new ArgumentException("Approval request hash must be a 64-character SHA-256 hex value.", nameof(hash));
        return hash.ToUpperInvariant();
    }

    private static string HashDecision(string key, ApprovalDecisionType decision, string? reason, long actorId)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            Key = key.Trim(),
            Decision = decision.ToString(),
            Reason = reason?.Trim(),
            ActorRealUserId = actorId
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static void EnsureTargetMatches(ApprovalTargetSnapshot? target, long version, string hash)
    {
        if (target is null || target.Version != version
            || !string.Equals(NormalizeHash(target.Hash), NormalizeHash(hash), StringComparison.Ordinal))
            throw new ApprovalKernelConflictException("Approval target version or content hash does not match the current record.");
    }

    private async Task EnsureTargetCurrentAsync(ApprovalInstance instance, CancellationToken cancellationToken)
    {
        var target = await targets.ReadAsync(
            instance.BusinessId, instance.TargetType, instance.TargetId, cancellationToken);
        EnsureTargetMatches(target, instance.TargetVersion, instance.RequestHash);
    }

    private static void EnsurePending(ApprovalInstance instance)
    {
        if (instance.Status != ApprovalInstanceStatus.Pending || instance.CurrentStepSequence is null)
            throw new ApprovalKernelConflictException("Approval instance is terminal and cannot accept another decision.");
    }

    private static ApprovalInstanceStep GetCurrentStep(ApprovalInstance instance) =>
        instance.Steps.SingleOrDefault(step => step.Sequence == instance.CurrentStepSequence
            && step.Decision == ApprovalStepDecision.Pending)
        ?? throw new ApprovalKernelUnavailableException("Approval instance has no valid current step.");

    private static void EnsureCandidate(
        ApprovalInstanceStep step,
        long actorId,
        long requesterId,
        bool allowSelfApproval)
    {
        if (actorId <= 0 || !allowSelfApproval && actorId == requesterId
            || !DeserializeCandidates(step).Contains(actorId))
            throw new UnauthorizedAccessException("Actor is not an eligible real-user approver for this step.");
    }

    private static IReadOnlyCollection<long> DeserializeCandidates(ApprovalInstanceStep step) =>
        JsonSerializer.Deserialize<long[]>(step.ResolvedApproverCandidatesJson)
        ?? throw new InvalidDataException($"Approval step {step.Id} has invalid frozen candidates.");

    private static void MarkOtherPendingSteps(ApprovalInstance instance, ApprovalStepDecision terminalProjection)
    {
        foreach (var step in instance.Steps.Where(item => item.Decision == ApprovalStepDecision.Pending))
        {
            step.Decision = terminalProjection;
        }
    }
}
