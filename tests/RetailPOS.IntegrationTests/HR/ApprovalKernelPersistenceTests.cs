using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.HR.Approvals;
using RetailPOS.Core.Audit;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Core.Entities.HR.Approvals;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.AuditEvents;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.HR.Approvals;

namespace RetailPOS.IntegrationTests.HR;

public sealed class ApprovalKernelPersistenceTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:;Foreign Keys=True");
    private readonly AuditContext _auditContext = new();
    private readonly FailAuditInterceptor _failure = new();
    private RetailPOSDbContext _db = null!;
    private ApprovalKernelHandlers _handlers = null!;
    private TestIdentity _identity = null!;
    private AuditService _auditService = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _db = new RetailPOSDbContext(new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(
                new AuditChangeTrackerInterceptor(_auditContext, NullLogger<AuditChangeTrackerInterceptor>.Instance),
                _failure)
            .Options);
        await _db.Database.EnsureCreatedAsync();
        _db.Businesses.Add(new Business { Id = 1, Name = "Approval Test Business" });
        _db.ApprovalPolicies.Add(CreatePolicy());
        await _db.SaveChangesAsync();

        _auditContext.Initialize(new AuditContextSnapshot(
            Guid.NewGuid(), 101, 101, 5, 7, null, 1, AuditSource.API,
            "POST", "/api/hr/approval-kernel-test", null, null, null, false, null, null, null, null,
            "requester", "HR"));
        _auditService = new AuditService(
            _db, _auditContext, Mock.Of<IServiceProvider>(), NullLogger<AuditService>.Instance);
        var requiredAudit = new RequiredAuditTransaction(
            _db, _auditContext, _auditService, NullLogger<RequiredAuditTransaction>.Instance);
        _identity = new TestIdentity();
        _handlers = new ApprovalKernelHandlers(
            new ApprovalKernelStore(_db), _identity, new AllowAccess(),
            new CandidateResolver(), new TargetSnapshotReader(), new NoOpApprovalKernelWorkflow(), requiredAudit);
    }

    [Fact]
    public async Task ApprovalWriteAndAuditCommitTogether_AndIdempotentReplayAddsNoAudit()
    {
        var submission = await SubmitAsync("target-1");
        Assert.True(submission.InstanceId > 0);
        Assert.Equal(1, await _db.AuditEvents.CountAsync());
        _identity.RealUserId = 202;

        var decision = await _handlers.Handle(
            new RecordApprovalDecisionCommand(submission.InstanceId, "approve-stable", ApprovalDecisionType.Approve, null),
            CancellationToken.None);
        Assert.Equal(ApprovalInstanceStatus.Approved, decision.Status);
        Assert.Equal(2, await _db.AuditEvents.CountAsync());
        Assert.Equal(1, await _db.ApprovalDecisions.CountAsync());

        var replay = await _handlers.Handle(
            new RecordApprovalDecisionCommand(submission.InstanceId, "approve-stable", ApprovalDecisionType.Approve, null),
            CancellationToken.None);
        Assert.True(replay.IsReplay);
        Assert.Equal(2, await _db.AuditEvents.CountAsync());
        Assert.Equal(1, await _db.ApprovalDecisions.CountAsync());
    }

    [Fact]
    public async Task AuditPersistenceFailureRollsBackApprovalInstanceAndStepWrites()
    {
        _failure.Fail = true;

        await Assert.ThrowsAsync<IOException>(() => SubmitAsync("target-fail"));

        Assert.Empty(await _db.ApprovalInstances.ToListAsync());
        Assert.Empty(await _db.ApprovalInstanceSteps.ToListAsync());
        Assert.Empty(await _db.AuditEvents.ToListAsync());
        Assert.False(_auditContext.HasActiveScope);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private Task<ApprovalDecisionResult> SubmitAsync(string targetId) =>
        _handlers.Handle(new SubmitApprovalCommand(
            ApprovalProcessType.Leave, "LeaveRequest", targetId, ApprovalScopeType.Business,
            null, 4, TestHash.Value), CancellationToken.None);

    private static ApprovalPolicy CreatePolicy()
    {
        var policy = new ApprovalPolicy
        {
            BusinessId = 1,
            Code = "LEAVE_TEST",
            ProcessType = ApprovalProcessType.Leave,
            Name = "Leave test policy",
            VersionNo = 1,
            ScopeType = ApprovalScopeType.Business,
            EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
            IsActive = true
        };
        policy.Steps.Add(new ApprovalPolicyStep
        {
            Sequence = 1,
            ApproverType = ApprovalApproverType.User,
            ApproverReference = "configured-user",
            RequiredApprovals = 1,
            CanReject = true,
            CanCancel = true
        });
        return policy;
    }

    private sealed class TestIdentity : IApprovalKernelIdentity
    {
        public long RealUserId { get; set; } = 101;
        public long RequireBusinessId() => 1;
        public long RequireRealUserId() => RealUserId;
        public long? ActingRoleId => 7;
        public Guid RequireCorrelationId() => Guid.NewGuid();
    }

    private sealed class AllowAccess : IApprovalKernelAccess
    {
        public Task EnsureAsync(
            long businessId, ApprovalProcessType processType, ApprovalKernelOperation operation,
            ApprovalScopeType scopeType, string? scopeReference, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class CandidateResolver : IApprovalCandidateResolver
    {
        public Task<IReadOnlyCollection<long>> ResolveAsync(
            long businessId, ApprovalPolicy policy, ApprovalPolicyStep step, long requesterUserId,
            ApprovalScopeType scopeType, string? scopeReference, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<long>>([202]);
    }

    private sealed class TargetSnapshotReader : IApprovalTargetSnapshotReader
    {
        public Task<ApprovalTargetSnapshot?> ReadAsync(
            long businessId, string targetType, string targetId, CancellationToken cancellationToken) =>
            Task.FromResult<ApprovalTargetSnapshot?>(new(4, TestHash.Value));
    }

    private sealed class FailAuditInterceptor : SaveChangesInterceptor
    {
        public bool Fail { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Fail && eventData.Context?.ChangeTracker.Entries<AuditEvent>()
                .Any(entry => entry.State == EntityState.Added) == true)
                throw new IOException("Injected approval Audit v2 persistence failure.");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private static class TestHash
    {
        public const string Value = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";
    }
}
