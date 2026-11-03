using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.HR.Approvals;
using RetailPOS.Application.HR.Employees;
using RetailPOS.Application.HR.Organisation;
using RetailPOS.Core.Audit;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Core.Entities.HR.Approvals;
using RetailPOS.Core.Entities.HR.Employees;
using RetailPOS.Core.Entities.HR.Organisation;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.AuditEvents;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.HR.Approvals;
using RetailPOS.Infrastructure.HR.Employees;

namespace RetailPOS.IntegrationTests.HR;

public sealed class HrEmployeeLocationTransferWorkflowTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:;Foreign Keys=True");
    private readonly AuditContext _auditContext = new();
    private readonly FailingAuditInterceptor _failure = new();
    private RetailPOSDbContext _db = null!;
    private HrEmployeeLocationAssignmentStore _store = null!;
    private ApprovalKernelHandlers _approval = null!;
    private TestIdentity _identity = null!;
    private TestCandidateResolver _candidateResolver = null!;
    private RequiredAuditTransaction _requiredAudit = null!;
    private DateOnly _today;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _db = new RetailPOSDbContext(new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(
                new AuditChangeTrackerInterceptor(_auditContext,
                    NullLogger<AuditChangeTrackerInterceptor>.Instance),
                _failure)
            .Options);
        await _db.Database.EnsureCreatedAsync();
        _today = DateOnly.FromDateTime(DateTime.UtcNow);
        _db.Businesses.Add(new Business { Id = 1, Name = "Employee Transfer Business" });
        _db.HrEmploymentTypes.Add(new HrEmploymentType
        {
            Id = 11, BusinessId = 1, Code = "Permanent", Name = "Permanent", IsActive = true
        });
        _db.HrEmployees.Add(new HrEmployee
        {
            Id = 101,
            BusinessId = 1,
            EmployeeCode = "EMP-XFER-1",
            FirstName = "Transfer",
            DisplayName = "Transfer Employee",
            CurrentEmploymentStatus = "Active",
            CurrentEmploymentTypeId = 11,
            JoiningDate = _today.AddDays(-30),
            CurrentPrimaryLocationProjectionId = 301,
            Version = 2
        });
        _db.HrEmploymentPeriods.Add(new HrEmploymentPeriod
        {
            Id = 201,
            BusinessId = 1,
            EmployeeId = 101,
            EmploymentTypeId = 11,
            StartDate = _today.AddDays(-30),
            Status = "Active",
            SourceEventType = "Joined"
        });
        _db.OperationalLocationProjections.AddRange(
            Location(301, "10", "Previous location"),
            Location(302, "20", "Transfer target"));
        _db.HrEmployeeLocationAssignments.Add(new HrEmployeeLocationAssignment
        {
            Id = 401,
            BusinessId = 1,
            EmployeeId = 101,
            OperationalLocationProjectionId = 301,
            LocationNameSnapshot = "Previous location",
            SourceSystemSnapshot = "POS",
            SourceEntityTypeSnapshot = "Outlet",
            SourceEntityIdSnapshot = "10",
            LocationSourceVersionSnapshot = 1,
            AssignmentType = "Primary",
            EffectiveFrom = _today.AddDays(-30),
            Status = "Effective",
            Reason = "Initial primary assignment",
            RequestedByUserId = 101,
            RequestedAtUtc = DateTime.UtcNow.AddDays(-30),
            Version = 1
        });
        _db.ApprovalPolicies.Add(CreatePolicy());
        await _db.SaveChangesAsync();

        _identity = new TestIdentity();
        _candidateResolver = new TestCandidateResolver();
        _store = new HrEmployeeLocationAssignmentStore(_db);
        var auditService = new AuditService(
            _db, _auditContext, Mock.Of<IServiceProvider>(), NullLogger<AuditService>.Instance);
        _requiredAudit = new RequiredAuditTransaction(
            _db, _auditContext, auditService, NullLogger<RequiredAuditTransaction>.Instance);
        _approval = new ApprovalKernelHandlers(
            new ApprovalKernelStore(_db),
            _identity,
            new AllowApprovalAccess(),
            _candidateResolver,
            new HrEmployeeLocationApprovalTargetSnapshotReader(_db),
            new HrEmployeeLocationApprovalWorkflow(_db),
            _requiredAudit);
        InitializeRequestAudit(101);
    }

    [Fact]
    public async Task FutureTransferStaysScheduledUntilDueThenActivatesOnceWithSnapshotAndAudit()
    {
        var effectiveFrom = _today.AddDays(2);
        var submission = await SubmitTransferAsync(effectiveFrom);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _approval.Handle(
            new RecordApprovalDecisionCommand(
                submission.ApprovalInstanceId, "self-approve", ApprovalDecisionType.Approve, null),
            CancellationToken.None));

        InitializeRequestAudit(202);
        var approved = await _approval.Handle(
            new RecordApprovalDecisionCommand(
                submission.ApprovalInstanceId, "eligible-approver", ApprovalDecisionType.Approve, "Approved"),
            CancellationToken.None);
        Assert.Equal(ApprovalInstanceStatus.Approved, approved.Status);

        var transfer = await _store.GetTransferAsync(1, 101, submission.TransferId, CancellationToken.None);
        Assert.Equal("ApprovedPendingEffective", transfer!.Status);
        Assert.Equal(301, await CurrentLocationAsync());
        Assert.Equal(1, await _db.HrEmployeeLocationAssignments.CountAsync());
        Assert.Equal(0, await ProcessDueAsync(effectiveFrom.AddDays(-1)));

        var targetLocation = await _db.OperationalLocationProjections.SingleAsync(row => row.Id == 302);
        targetLocation.Name = "Renamed after approval";
        await _db.SaveChangesAsync();

        Assert.Equal(1, await ProcessDueAsync(effectiveFrom));
        Assert.Equal(1, await _db.HrEmployeeLocationAssignments.CountAsync(row =>
            row.BusinessId == 1 && row.EmployeeId == 101 && row.Status == "Effective"));
        var assignments = await _db.HrEmployeeLocationAssignments
            .Where(row => row.BusinessId == 1 && row.EmployeeId == 101)
            .OrderBy(row => row.EffectiveFrom)
            .ToArrayAsync();
        Assert.Equal(effectiveFrom.AddDays(-1), assignments[0].EffectiveTo);
        Assert.Equal(effectiveFrom, assignments[1].EffectiveFrom);
        Assert.Equal("Transfer target", assignments[1].LocationNameSnapshot);
        Assert.Equal(302, await CurrentLocationAsync());
        Assert.Equal("Effective", (await _store.GetTransferAsync(
            1, 101, submission.TransferId, CancellationToken.None))!.Status);
        Assert.Equal(0, await ProcessDueAsync(effectiveFrom));
        Assert.Equal(1, await _db.AuditEvents.CountAsync(row =>
            row.ActionType == "hr.employee_location_transfer.processed"));
    }

    [Fact]
    public async Task ApprovalEffectiveTodayAppliesImmediately()
    {
        var submission = await SubmitTransferAsync(_today);
        await ApproveTransferAsync(submission.ApprovalInstanceId);

        Assert.Equal(302, await CurrentLocationAsync());
        var current = await _db.HrEmployeeLocationAssignments.SingleAsync(row =>
            row.EmployeeId == 101 && row.OperationalLocationProjectionId == 302);
        Assert.Equal(_today, current.EffectiveFrom);
        Assert.Equal("Effective", (await _store.GetTransferAsync(
            1, 101, submission.TransferId, CancellationToken.None))!.Status);
    }

    [Fact]
    public async Task CancelledScheduledTransferNeverActivates()
    {
        var effectiveFrom = _today.AddDays(1);
        var submission = await SubmitTransferAsync(effectiveFrom);
        await ApproveTransferAsync(submission.ApprovalInstanceId);
        InitializeRequestAudit(101);
        var cancellation = new CancelHrEmployeeLocationTransferCommandHandler(
            new TestOrganisationAccess(), _store, Mock.Of<MediatR.ISender>(), _requiredAudit);
        var result = await cancellation.Handle(
            new CancelHrEmployeeLocationTransferCommand(
                101, submission.TransferId, "cancel-scheduled", "Changed plans"),
            CancellationToken.None);

        Assert.Equal("Cancelled", result.TargetStatus);
        Assert.Equal(0, await ProcessDueAsync(effectiveFrom));
        Assert.Equal(301, await CurrentLocationAsync());
        Assert.Equal("Cancelled", (await _store.GetTransferAsync(
            1, 101, submission.TransferId, CancellationToken.None))!.Status);
    }

    [Fact]
    public async Task RejectedTransferNeverActivates()
    {
        var effectiveFrom = _today.AddDays(1);
        var submission = await SubmitTransferAsync(effectiveFrom);
        InitializeRequestAudit(202);
        var decision = await _approval.Handle(
            new RecordApprovalDecisionCommand(
                submission.ApprovalInstanceId, "reject-transfer", ApprovalDecisionType.Reject, "Not approved"),
            CancellationToken.None);

        Assert.Equal(ApprovalInstanceStatus.Rejected, decision.Status);
        Assert.Equal(0, await ProcessDueAsync(effectiveFrom));
        Assert.Equal(301, await CurrentLocationAsync());
        Assert.Equal("Rejected", (await _store.GetTransferAsync(
            1, 101, submission.TransferId, CancellationToken.None))!.Status);
    }

    [Fact]
    public async Task StaleScheduledTransferIsSupersededWithoutChangingCurrentLocation()
    {
        var effectiveFrom = _today.AddDays(1);
        var submission = await SubmitTransferAsync(effectiveFrom);
        await ApproveTransferAsync(submission.ApprovalInstanceId);
        var employee = await _db.HrEmployees.SingleAsync(row => row.Id == 101);
        employee.Version++;
        await _db.SaveChangesAsync();

        Assert.Equal(1, await ProcessDueAsync(effectiveFrom));
        var transfer = await _store.GetTransferAsync(1, 101, submission.TransferId, CancellationToken.None);
        Assert.Equal("Superseded", transfer!.Status);
        Assert.Equal(301, await CurrentLocationAsync());
        Assert.Equal(0, await ProcessDueAsync(effectiveFrom));
    }

    [Fact]
    public async Task SecondPendingOrScheduledPrimaryTransferIsRejected()
    {
        var first = await SubmitTransferAsync(_today.AddDays(2));
        await ApproveTransferAsync(first.ApprovalInstanceId);

        await Assert.ThrowsAsync<ApprovalKernelConflictException>(() =>
            SubmitTransferAsync(_today.AddDays(3)));
        Assert.Equal(1, await _db.HrEmployeeLocationTransferRequests.CountAsync());
        Assert.Equal(301, await CurrentLocationAsync());
    }

    [Fact]
    public async Task MissingPolicyAndEligibleApproverFailClosed()
    {
        var policy = await _db.ApprovalPolicies.SingleAsync();
        policy.IsActive = false;
        await _db.SaveChangesAsync();
        await Assert.ThrowsAsync<ApprovalKernelUnavailableException>(() =>
            SubmitTransferAsync(_today.AddDays(1)));
        Assert.Empty(await _db.HrEmployeeLocationTransferRequests.ToListAsync());
        Assert.Empty(await _db.ApprovalInstances.ToListAsync());

        policy.IsActive = true;
        await _db.SaveChangesAsync();
        _candidateResolver.Candidates = [];
        await Assert.ThrowsAsync<ApprovalKernelUnavailableException>(() =>
            SubmitTransferAsync(_today.AddDays(1)));
        Assert.Empty(await _db.HrEmployeeLocationTransferRequests.ToListAsync());
        Assert.Empty(await _db.ApprovalInstances.ToListAsync());
    }

    [Fact]
    public async Task AuditFailureRollsBackDueActivation()
    {
        var effectiveFrom = _today.AddDays(1);
        var submission = await SubmitTransferAsync(effectiveFrom);
        await ApproveTransferAsync(submission.ApprovalInstanceId);
        _failure.Fail = true;

        await Assert.ThrowsAsync<IOException>(() => ProcessDueAsync(effectiveFrom));

        Assert.Equal(301, await CurrentLocationAsync());
        Assert.Equal(1, await _db.HrEmployeeLocationAssignments.CountAsync());
        Assert.Equal("ApprovedPendingEffective", (await _store.GetTransferAsync(
            1, 101, submission.TransferId, CancellationToken.None))!.Status);
        Assert.False(_auditContext.HasActiveScope);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private async Task<HrEmployeeLocationTransferSubmissionResult> SubmitTransferAsync(DateOnly effectiveFrom)
    {
        InitializeRequestAudit(101);
        var payload = new HrEmployeeLocationTransferPayload(
            Guid.NewGuid(), 101, 302, effectiveFrom, 2, "Employee transfer test");
        var serialized = JsonSerializer.Serialize(payload);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(serialized)));
        var result = await _approval.Handle(new SubmitApprovalCommand(
            ApprovalProcessType.EmployeeTransfer,
            HrEmployeeLocationTransferConstants.TargetType,
            payload.TransferId.ToString("D"),
            ApprovalScopeType.Location,
            "302",
            1,
            hash,
            serialized), CancellationToken.None);
        return new HrEmployeeLocationTransferSubmissionResult(
            payload.TransferId, result.InstanceId, result.Status.ToString());
    }

    private async Task ApproveTransferAsync(long instanceId)
    {
        InitializeRequestAudit(202);
        await _approval.Handle(new RecordApprovalDecisionCommand(
            instanceId, Guid.NewGuid().ToString("N"), ApprovalDecisionType.Approve, "Approved"),
            CancellationToken.None);
    }

    private async Task<int> ProcessDueAsync(DateOnly date) =>
        await new ProcessDueHrEmployeeLocationTransfersCommandHandler(
                _store, _requiredAudit, _auditContext)
            .Handle(new ProcessDueHrEmployeeLocationTransfersCommand(1, date), CancellationToken.None);

    private Task<long?> CurrentLocationAsync() =>
        _db.HrEmployees.AsNoTracking().Where(row => row.BusinessId == 1 && row.Id == 101)
            .Select(row => row.CurrentPrimaryLocationProjectionId).SingleAsync();

    private void InitializeRequestAudit(long actorUserId)
    {
        _identity.RealUserId = actorUserId;
        _auditContext.Initialize(new AuditContextSnapshot(
            Guid.NewGuid(), actorUserId, actorUserId, 5, 7, null, 1, AuditSource.API,
            "POST", "/api/hr/v1/employees/101/location-transfers", null, null, null, false,
            null, null, null, null, "test-user", "HR"));
    }

    private static ApprovalPolicy CreatePolicy()
    {
        var policy = new ApprovalPolicy
        {
            BusinessId = 1,
            Code = "EMPLOYEE_TRANSFER_TEST",
            ProcessType = ApprovalProcessType.EmployeeTransfer,
            Name = "Employee transfer integration test",
            VersionNo = 1,
            ScopeType = ApprovalScopeType.Location,
            ScopeReference = "302",
            AllowSelfApproval = false,
            EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1),
            IsActive = true
        };
        policy.Steps.Add(new ApprovalPolicyStep
        {
            Sequence = 1,
            ApproverType = ApprovalApproverType.User,
            ApproverReference = "test-eligible-user",
            RequiredApprovals = 1,
            CanReject = true,
            CanCancel = true
        });
        return policy;
    }

    private static OperationalLocationProjection Location(long id, string sourceId, string name) => new()
    {
        Id = id,
        BusinessId = 1,
        SourceSystem = "POS",
        SourceEntityType = "Outlet",
        SourceEntityId = sourceId,
        Name = name,
        SourceVersion = 1,
        SourcePayloadHash = "hash-" + sourceId,
        SourceUpdatedAtUtc = DateTime.UtcNow,
        LastSyncedAtUtc = DateTime.UtcNow,
        IsSourceActive = true,
        SyncStatus = "Current"
    };

    private sealed class TestIdentity : IApprovalKernelIdentity
    {
        public long RealUserId { get; set; } = 101;
        public long RequireBusinessId() => 1;
        public long RequireRealUserId() => RealUserId;
        public long? ActingRoleId => 7;
        public Guid RequireCorrelationId() => Guid.NewGuid();
    }

    private sealed class AllowApprovalAccess : IApprovalKernelAccess
    {
        public Task EnsureAsync(
            long businessId,
            ApprovalProcessType processType,
            ApprovalKernelOperation operation,
            ApprovalScopeType scopeType,
            string? scopeReference,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class TestCandidateResolver : IApprovalCandidateResolver
    {
        public IReadOnlyCollection<long> Candidates { get; set; } = [101, 202];

        public Task<IReadOnlyCollection<long>> ResolveAsync(
            long businessId,
            ApprovalPolicy policy,
            ApprovalPolicyStep step,
            long requesterUserId,
            ApprovalScopeType scopeType,
            string? scopeReference,
            CancellationToken cancellationToken) =>
            Task.FromResult(Candidates);
    }

    private sealed class FailingAuditInterceptor : SaveChangesInterceptor
    {
        public bool Fail { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Fail && eventData.Context?.ChangeTracker.Entries<AuditEvent>()
                .Any(entry => entry.State == EntityState.Added) == true)
                throw new IOException("Injected Audit v2 persistence failure.");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class TestOrganisationAccess : IHrOrganisationAccess
    {
        public long RequireBusinessId() => 1;
        public long RequireRealUserId() => 101;

        public Task EnsureAsync(
            long businessId, HrOrganisationOperation operation, long? locationProjectionId,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<HrOperationalLocationReadScope> GetLocationReadScopeAsync(
            long businessId, CancellationToken cancellationToken) =>
            Task.FromResult(new HrOperationalLocationReadScope(businessId, null));

        public Task EnsureLocationAccessibleAsync(
            long businessId, long locationProjectionId, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
