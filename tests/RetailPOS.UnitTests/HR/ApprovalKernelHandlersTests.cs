using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.HR.Approvals;
using RetailPOS.Core.Entities.HR.Approvals;

namespace RetailPOS.UnitTests.HR;

public sealed class ApprovalKernelHandlersTests
{
    [Fact]
    public async Task SingleLevelApproval_CompletesInstanceAndRecordsDecision()
    {
        var harness = new Harness([202]);
        var instance = await harness.SubmitAsync();
        harness.Identity.RealUserId = 202;

        var result = await harness.DecideAsync(instance.InstanceId, "approve-1");

        Assert.Equal(ApprovalInstanceStatus.Approved, result.Status);
        Assert.Null(result.CurrentStepSequence);
        Assert.Single(harness.Store.Decisions);
        Assert.Equal(2, harness.Audit.AuditedOperations);
    }

    [Fact]
    public async Task MultiStepApproval_AdvancesSequentiallyUsingFrozenCandidates()
    {
        var harness = new Harness([202], [303]);
        var instance = await harness.SubmitAsync();
        harness.Identity.RealUserId = 202;

        var first = await harness.DecideAsync(instance.InstanceId, "step-1");

        Assert.Equal(ApprovalInstanceStatus.Pending, first.Status);
        Assert.Equal(2, first.CurrentStepSequence);
        Assert.Equal(ApprovalStepDecision.Approved, harness.Instance.Steps.Single(step => step.Sequence == 1).Decision);
        Assert.Equal(ApprovalStepDecision.Pending, harness.Instance.Steps.Single(step => step.Sequence == 2).Decision);

        harness.Identity.RealUserId = 303;
        var second = await harness.DecideAsync(instance.InstanceId, "step-2");
        Assert.Equal(ApprovalInstanceStatus.Approved, second.Status);
    }

    [Fact]
    public async Task QuorumRequiresDistinctRealUsers()
    {
        var harness = new Harness([202, 303], requiredApprovals: 2);
        var instance = await harness.SubmitAsync();
        harness.Identity.RealUserId = 202;

        var first = await harness.DecideAsync(instance.InstanceId, "quorum-1");
        Assert.Equal(ApprovalInstanceStatus.Pending, first.Status);
        Assert.Equal(ApprovalStepDecision.Pending, harness.Instance.Steps.Single().Decision);

        harness.Identity.RealUserId = 303;
        var second = await harness.DecideAsync(instance.InstanceId, "quorum-2");
        Assert.Equal(ApprovalInstanceStatus.Approved, second.Status);
        Assert.Equal(2, harness.Store.Decisions.Count);
    }

    [Fact]
    public async Task DuplicateDecisionKeyWithSamePayloadReturnsPriorResultWithoutAnotherAudit()
    {
        var harness = new Harness([202]);
        var instance = await harness.SubmitAsync();
        harness.Identity.RealUserId = 202;

        var first = await harness.DecideAsync(instance.InstanceId, "retry-key");
        var auditedAfterFirst = harness.Audit.AuditedOperations;
        var retry = await harness.DecideAsync(instance.InstanceId, "retry-key");

        Assert.False(first.IsReplay);
        Assert.True(retry.IsReplay);
        Assert.Equal(first.Status, retry.Status);
        Assert.Single(harness.Store.Decisions);
        Assert.Equal(auditedAfterFirst, harness.Audit.AuditedOperations);
    }

    [Fact]
    public async Task DuplicateDecisionKeyWithChangedPayloadConflicts()
    {
        var harness = new Harness([202, 303], requiredApprovals: 2);
        var instance = await harness.SubmitAsync();
        harness.Identity.RealUserId = 202;
        await harness.DecideAsync(instance.InstanceId, "retry-key");

        await Assert.ThrowsAsync<ApprovalKernelConflictException>(() =>
            harness.DecideAsync(instance.InstanceId, "retry-key", ApprovalDecisionType.Reject, "different"));
        Assert.Single(harness.Store.Decisions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DecisionKeyCannotReplayAgainstAnotherInstance(bool cancel)
    {
        var harness = new Harness([202]);
        var instance = await harness.SubmitAsync();
        harness.Identity.RealUserId = cancel ? 101 : 202;
        if (cancel)
            await harness.CancelAsync(instance.InstanceId, "bound-key", "Withdraw");
        else
            await harness.DecideAsync(instance.InstanceId, "bound-key");
        var other = new ApprovalInstance
        {
            BusinessId = 1, ProcessType = harness.Instance.ProcessType,
            ScopeType = harness.Instance.ScopeType
        };
        harness.Store.AddInstance(other);

        await Assert.ThrowsAsync<ApprovalKernelConflictException>(() => cancel
            ? harness.CancelAsync(other.Id, "bound-key", "Withdraw")
            : harness.DecideAsync(other.Id, "bound-key"));
        Assert.Single(harness.Store.Decisions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DecisionReplayRechecksCurrentAuthorization(bool cancel)
    {
        var harness = new Harness([202]);
        var instance = await harness.SubmitAsync();
        harness.Identity.RealUserId = cancel ? 101 : 202;
        if (cancel)
            await harness.CancelAsync(instance.InstanceId, "access-key", "Withdraw");
        else
            await harness.DecideAsync(instance.InstanceId, "access-key");
        harness.Access.Denied = true;

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => cancel
            ? harness.CancelAsync(instance.InstanceId, "access-key", "Withdraw")
            : harness.DecideAsync(instance.InstanceId, "access-key"));
        Assert.Single(harness.Store.Decisions);
    }

    [Fact]
    public async Task ConcurrentSameKeyApprovalAttemptsCreateOneDecisionAndAudit()
    {
        var harness = new Harness([202]);
        var instance = await harness.SubmitAsync();
        harness.Identity.RealUserId = 202;
        harness.Audit.PauseNextOperation();

        var firstAttempt = harness.DecideAsync(instance.InstanceId, "concurrent-key");
        await harness.Audit.FirstOperationHeld;
        var secondAttempt = harness.DecideAsync(instance.InstanceId, "concurrent-key");
        await harness.Audit.SecondAttemptEntered;
        harness.Audit.ReleaseFirstOperation();
        var results = await Task.WhenAll(firstAttempt, secondAttempt);

        Assert.Single(harness.Store.Decisions);
        Assert.Equal(1, results.Count(result => result.IsReplay));
        Assert.Equal(2, harness.Audit.AuditedOperations);
    }

    [Fact]
    public async Task SelfApprovalIsDeniedEvenWhenActingRoleChanges()
    {
        var harness = new Harness([101]);
        await Assert.ThrowsAsync<ApprovalKernelUnavailableException>(() => harness.SubmitAsync());

        var second = new Harness([202]);
        var instance = await second.SubmitAsync();
        second.Identity.RealUserId = 101;
        second.Identity.ActingRoleId = 99;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            second.DecideAsync(instance.InstanceId, "self-by-role"));
    }

    [Fact]
    public async Task RejectionIsTerminal()
    {
        var harness = new Harness([202]);
        var instance = await harness.SubmitAsync();
        harness.Identity.RealUserId = 202;
        var rejected = await harness.DecideAsync(instance.InstanceId, "reject-1", ApprovalDecisionType.Reject, "not eligible");

        Assert.Equal(ApprovalInstanceStatus.Rejected, rejected.Status);
        await Assert.ThrowsAsync<ApprovalKernelConflictException>(() =>
            harness.DecideAsync(instance.InstanceId, "approve-after-reject"));
    }

    [Fact]
    public async Task CancellationIsTerminal()
    {
        var harness = new Harness([202]);
        var instance = await harness.SubmitAsync();

        var cancelled = await harness.CancelAsync(instance.InstanceId, "cancel-1", "request withdrawn");

        Assert.Equal(ApprovalInstanceStatus.Cancelled, cancelled.Status);
        await Assert.ThrowsAsync<ApprovalKernelConflictException>(() =>
            harness.DecideAsync(instance.InstanceId, "approve-after-cancel"));
    }

    [Fact]
    public async Task PayrollRunPoliciesMustBeSingleLevelAndForbidSelfApproval()
    {
        var harness = new Harness([202]);
        var selfApproval = new CreateApprovalPolicyVersionCommand(
            "PAYROLL_RUN", ApprovalProcessType.PayrollRun, "Payroll Run", 1,
            ApprovalScopeType.Business, null, true, DateOnly.FromDateTime(DateTime.UtcNow),
            null, true, [new ApprovalPolicyStepInput(1, ApprovalApproverType.User, "202", 1, true, false)]);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            harness.CreatePolicyAsync(selfApproval));

        var multiLevel = selfApproval with
        {
            AllowSelfApproval = false,
            Steps =
            [
                new ApprovalPolicyStepInput(1, ApprovalApproverType.User, "202", 1, true, false),
                new ApprovalPolicyStepInput(2, ApprovalApproverType.User, "303", 1, true, false)
            ]
        };
        await Assert.ThrowsAsync<ArgumentException>(() =>
            harness.CreatePolicyAsync(multiLevel));
        Assert.DoesNotContain(harness.Store.Policies,
            row => row.ProcessType == ApprovalProcessType.PayrollRun);
    }

    [Fact]
    public async Task ChangedTargetVersionOrHashBlocksDecision()
    {
        var harness = new Harness([202]);
        var instance = await harness.SubmitAsync();
        harness.Identity.RealUserId = 202;
        harness.Targets.Current = new ApprovalTargetSnapshot(4, Harness.TargetHash);

        await Assert.ThrowsAsync<ApprovalKernelConflictException>(() =>
            harness.DecideAsync(instance.InstanceId, "stale-target"));
        Assert.Empty(harness.Store.Decisions);
    }

    [Fact]
    public async Task PolicyVersionChangeDoesNotRewriteFrozenApprovalInstance()
    {
        var harness = new Harness([202]);
        var instance = await harness.SubmitAsync();
        var frozenCandidates = harness.Instance.Steps.Single().ResolvedApproverCandidatesJson;
        harness.Store.AddPolicy(Harness.Policy(version: 2, approverIds: [303]));
        harness.Identity.RealUserId = 202;

        var approved = await harness.DecideAsync(instance.InstanceId, "frozen-policy");

        Assert.Equal(ApprovalInstanceStatus.Approved, approved.Status);
        Assert.Equal(1, harness.Instance.PolicyVersionSnapshot);
        Assert.Equal(frozenCandidates, harness.Instance.Steps.Single().ResolvedApproverCandidatesJson);
    }

    [Fact]
    public async Task MissingPolicyAndMissingCandidatesFailClosed()
    {
        var missingPolicy = new Harness([202], includePolicy: false);
        await Assert.ThrowsAsync<ApprovalKernelUnavailableException>(() => missingPolicy.SubmitAsync());

        var missingApprovers = new Harness([]);
        await Assert.ThrowsAsync<ApprovalKernelUnavailableException>(() => missingApprovers.SubmitAsync());
        Assert.Empty(missingApprovers.Store.Instances);
    }

    [Fact]
    public async Task CrossBusinessInstanceAccessIsDenied()
    {
        var harness = new Harness([202]);
        var instance = await harness.SubmitAsync();
        harness.Identity.BusinessId = 2;
        harness.Identity.RealUserId = 202;

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            harness.DecideAsync(instance.InstanceId, "cross-business"));
        Assert.Empty(harness.Store.Decisions);
    }

    private sealed class Harness
    {
        public const string TargetHash = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";
        public readonly FakeStore Store = new();
        public readonly FakeIdentity Identity = new();
        public readonly FakeAudit Audit = new();
        public readonly FakeTargetReader Targets = new();
        public readonly AllowAccess Access = new();
        private readonly FakeCandidates _candidates;
        private readonly ApprovalKernelHandlers _handlers;

        public Harness(
            long[] firstStepCandidates,
            long[]? secondStepCandidates = null,
            int requiredApprovals = 1,
            bool includePolicy = true)
        {
            if (includePolicy)
                Store.AddPolicy(Policy(requiredApprovals, firstStepCandidates, secondStepCandidates));
            _candidates = new FakeCandidates();
            _candidates.BySequence[1] = firstStepCandidates;
            if (secondStepCandidates is not null)
                _candidates.BySequence[2] = secondStepCandidates;
            Targets.Current = new ApprovalTargetSnapshot(3, TargetHash);
            _handlers = new ApprovalKernelHandlers(
                Store, Identity, Access, _candidates, Targets, new NoOpApprovalKernelWorkflow(), Audit);
        }

        public ApprovalInstance Instance => Assert.Single(Store.Instances);

        public Task<ApprovalDecisionResult> SubmitAsync() => _handlers.Handle(new SubmitApprovalCommand(
            ApprovalProcessType.Leave, "LeaveRequest", "77", ApprovalScopeType.Business, null,
            3, TargetHash), CancellationToken.None);

        public Task<ApprovalDecisionResult> DecideAsync(
            long instanceId,
            string key,
            ApprovalDecisionType decision = ApprovalDecisionType.Approve,
            string? reason = null) =>
            _handlers.Handle(new RecordApprovalDecisionCommand(instanceId, key, decision, reason), CancellationToken.None);

        public Task<ApprovalDecisionResult> CancelAsync(long instanceId, string key, string reason) =>
            _handlers.Handle(new CancelApprovalCommand(instanceId, key, reason), CancellationToken.None);

        public Task<long> CreatePolicyAsync(CreateApprovalPolicyVersionCommand command) =>
            _handlers.Handle(command, CancellationToken.None);

        public static ApprovalPolicy Policy(
            int requiredApprovals = 1,
            long[]? approverIds = null,
            long[]? secondStepApproverIds = null,
            int version = 1)
        {
            var policy = new ApprovalPolicy
            {
                Id = version,
                BusinessId = 1,
                Code = "LEAVE_DEFAULT",
                ProcessType = ApprovalProcessType.Leave,
                Name = "Leave",
                VersionNo = version,
                ScopeType = ApprovalScopeType.Business,
                EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
                IsActive = true
            };
            policy.Steps.Add(new ApprovalPolicyStep
            {
                Id = 100 + version,
                Sequence = 1,
                ApproverType = ApprovalApproverType.User,
                ApproverReference = "explicit",
                RequiredApprovals = requiredApprovals,
                CanReject = true,
                CanCancel = true
            });
            if (secondStepApproverIds is not null)
            {
                policy.Steps.Add(new ApprovalPolicyStep
                {
                    Id = 200 + version,
                    Sequence = 2,
                    ApproverType = ApprovalApproverType.User,
                    ApproverReference = "explicit",
                    RequiredApprovals = 1,
                    CanReject = true,
                    CanCancel = true
                });
            }
            return policy;
        }
    }

    private sealed class FakeIdentity : IApprovalKernelIdentity
    {
        public long BusinessId { get; set; } = 1;
        public long RealUserId { get; set; } = 101;
        public long? ActingRoleId { get; set; } = 7;
        public long RequireBusinessId() => BusinessId;
        public long RequireRealUserId() => RealUserId;
        public Guid RequireCorrelationId() => Guid.NewGuid();
    }

    private sealed class FakeStore : IApprovalKernelStore
    {
        private long _nextInstanceId = 1;
        private long _nextStepId = 1;
        public List<ApprovalPolicy> Policies { get; } = [];
        public List<ApprovalInstance> Instances { get; } = [];
        public List<ApprovalDecision> Decisions { get; } = [];

        public Task<int> GetLatestPolicyVersionAsync(long businessId, string code, CancellationToken cancellationToken) =>
            Task.FromResult(Policies.Where(policy => policy.BusinessId == businessId && policy.Code == code)
                .Select(policy => policy.VersionNo).DefaultIfEmpty().Max());

        public Task<ApprovalPolicy?> FindEffectivePolicyAsync(
            long businessId, ApprovalProcessType processType, ApprovalScopeType scopeType,
            string? scopeReference, DateOnly effectiveOn, CancellationToken cancellationToken) =>
            Task.FromResult(Policies.SingleOrDefault(policy => policy.BusinessId == businessId
                && policy.ProcessType == processType && policy.ScopeType == scopeType
                && policy.ScopeReference == scopeReference && policy.IsActive
                && policy.EffectiveFrom <= effectiveOn
                && (policy.EffectiveTo is null || policy.EffectiveTo >= effectiveOn)));

        public Task<ApprovalDecision?> FindDecisionByKeyAsync(
            long businessId, string decisionKey, CancellationToken cancellationToken) =>
            Task.FromResult(Decisions.SingleOrDefault(decision =>
                decision.BusinessId == businessId && decision.DecisionKey == decisionKey));

        public Task<ApprovalInstance?> FindInstanceForUpdateAsync(
            long businessId, long instanceId, CancellationToken cancellationToken) =>
            Task.FromResult(Instances.SingleOrDefault(instance =>
                instance.BusinessId == businessId && instance.Id == instanceId));

        public Task<IReadOnlyList<ApprovalDecision>> GetStepDecisionsAsync(
            long businessId, long stepId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ApprovalDecision>>(Decisions
                .Where(decision => decision.BusinessId == businessId && decision.ApprovalInstanceStepId == stepId)
                .ToArray());

        public void AddPolicy(ApprovalPolicy policy)
        {
            if (policy.Id == 0)
                policy.Id = Policies.Select(existing => existing.Id).DefaultIfEmpty().Max() + 1;
            Policies.Add(policy);
        }

        public void AddInstance(ApprovalInstance instance)
        {
            instance.Id = _nextInstanceId++;
            foreach (var step in instance.Steps)
                step.Id = _nextStepId++;
            Instances.Add(instance);
        }

        public Task PersistSubmissionAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public void AddDecision(ApprovalDecision decision) => Decisions.Add(decision);
    }

    private sealed class FakeCandidates : IApprovalCandidateResolver
    {
        public Dictionary<int, long[]> BySequence { get; } = [];
        public Task<IReadOnlyCollection<long>> ResolveAsync(
            long businessId, ApprovalPolicy policy, ApprovalPolicyStep step, long requesterUserId,
            ApprovalScopeType scopeType, string? scopeReference, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<long>>(BySequence.GetValueOrDefault(step.Sequence) ?? []);
    }

    private sealed class FakeTargetReader : IApprovalTargetSnapshotReader
    {
        public ApprovalTargetSnapshot? Current { get; set; }
        public Task<ApprovalTargetSnapshot?> ReadAsync(
            long businessId, string targetType, string targetId, CancellationToken cancellationToken) =>
            Task.FromResult(Current);
    }

    private sealed class AllowAccess : IApprovalKernelAccess
    {
        public bool Denied { get; set; }
        public Task EnsureAsync(
            long businessId, ApprovalProcessType processType, ApprovalKernelOperation operation,
            ApprovalScopeType scopeType, string? scopeReference, CancellationToken cancellationToken) =>
            Denied ? Task.FromException(new UnauthorizedAccessException("Access was revoked.")) : Task.CompletedTask;
    }

    private sealed class FakeAudit : IRequiredAuditTransaction
    {
        private readonly SemaphoreSlim _transactionGate = new(1, 1);
        private TaskCompletionSource _firstOperationHeld = NewSignal();
        private TaskCompletionSource _secondAttemptEntered = NewSignal();
        private TaskCompletionSource _releaseFirstOperation = NewSignal();
        private int _pauseNextOperation;
        private int _concurrentAttemptCount;
        public int AuditedOperations { get; private set; }

        public Task FirstOperationHeld => _firstOperationHeld.Task;
        public Task SecondAttemptEntered => _secondAttemptEntered.Task;

        public void PauseNextOperation()
        {
            _firstOperationHeld = NewSignal();
            _secondAttemptEntered = NewSignal();
            _releaseFirstOperation = NewSignal();
            _concurrentAttemptCount = 0;
            _pauseNextOperation = 1;
        }

        public void ReleaseFirstOperation() => _releaseFirstOperation.TrySetResult();

        public Task<T> ExecuteAsync<T>(
            RequiredAuditOperation operation,
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken) =>
            ExecuteAsync(operation, action, _ => true, cancellationToken);

        public async Task<T> ExecuteAsync<T>(
            RequiredAuditOperation operation,
            Func<CancellationToken, Task<T>> action,
            Func<T, bool> requiresAudit,
            CancellationToken cancellationToken)
        {
            var concurrentAttempt = _pauseNextOperation == 1
                ? Interlocked.Increment(ref _concurrentAttemptCount)
                : 0;
            if (concurrentAttempt == 2)
                _secondAttemptEntered.TrySetResult();

            await _transactionGate.WaitAsync(cancellationToken);
            try
            {
                if (concurrentAttempt == 1)
                {
                    _firstOperationHeld.TrySetResult();
                    await _releaseFirstOperation.Task.WaitAsync(cancellationToken);
                }
                var result = await action(cancellationToken);
                if (requiresAudit(result))
                    AuditedOperations++;
                return result;
            }
            finally
            {
                _transactionGate.Release();
            }
        }

        private static TaskCompletionSource NewSignal() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
