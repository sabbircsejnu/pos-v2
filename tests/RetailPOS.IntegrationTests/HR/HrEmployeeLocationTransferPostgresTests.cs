using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.HR.Approvals;
using RetailPOS.Application.HR.Employees;
using RetailPOS.Core.Audit;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Core.Entities.HR.Approvals;
using RetailPOS.Core.Entities.HR.Employees;
using RetailPOS.Core.Entities.HR.Organisation;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.AuditEvents;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.HR.Employees;
using RetailPOS.Infrastructure.HR.Approvals;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.IntegrationTests.HR;

public sealed class HrEmployeeLocationTransferPostgresTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PostgreSqlSerializesTransferRequestsAndDueActivation(bool sameDecisionKey)
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        var connectionString = Environment.GetEnvironmentVariable("RETAILPOS_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            var settingsPath = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "..",
                "src", "RetailPOS.API", "appsettings.json"));
            connectionString = new ConfigurationBuilder()
                .AddJsonFile(settingsPath, optional: false)
                .Build()
                .GetConnectionString("DefaultConnection");
        }

        Assert.False(string.IsNullOrWhiteSpace(connectionString));
        await using var database = await PostgresTestDatabase.CreateAsync(
            connectionString!, PostgresTestDatabasePrefix.MigrationBaseline);
        var options = new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseNpgsql(database.ConnectionString)
            .Options;
        const long businessId = 98571;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        long employeeId;

        await using (var setup = new RetailPOSDbContext(options))
        {
            await setup.Database.MigrateAsync();
            setup.Businesses.Add(new Business { Id = businessId, Name = "Transfer concurrency test" });
            await setup.SaveChangesAsync();
            var employmentType = new HrEmploymentType
            {
                BusinessId = businessId,
                Code = "PERM-XFER",
                Name = "Permanent",
                IsActive = true
            };
            setup.HrEmploymentTypes.Add(employmentType);
            setup.OperationalLocationProjections.AddRange(
                CreateLocation(businessId, 301, "10", "Primary"),
                CreateLocation(businessId, 302, "20", "Transfer one"),
                CreateLocation(businessId, 303, "30", "Transfer two"));
            await setup.SaveChangesAsync();
            var employee = new HrEmployee
            {
                BusinessId = businessId,
                EmployeeCode = "EMP-XFER-PG",
                FirstName = "Concurrent",
                DisplayName = "Concurrent Transfer",
                CurrentEmploymentStatus = "Active",
                CurrentEmploymentTypeId = employmentType.Id,
                JoiningDate = today.AddDays(-10),
                CurrentPrimaryLocationProjectionId = 301,
                Version = 1
            };
            setup.HrEmployees.Add(employee);
            await setup.SaveChangesAsync();
            employeeId = employee.Id;
            setup.HrEmploymentPeriods.Add(new HrEmploymentPeriod
            {
                BusinessId = businessId,
                EmployeeId = employeeId,
                EmploymentTypeId = employmentType.Id,
                StartDate = today.AddDays(-10),
                Status = "Active",
                SourceEventType = "Joined"
            });
            setup.HrEmployeeLocationAssignments.Add(new HrEmployeeLocationAssignment
            {
                BusinessId = businessId,
                EmployeeId = employeeId,
                OperationalLocationProjectionId = 301,
                LocationNameSnapshot = "Primary",
                SourceSystemSnapshot = "POS",
                SourceEntityTypeSnapshot = "Outlet",
                SourceEntityIdSnapshot = "10",
                LocationSourceVersionSnapshot = 1,
                AssignmentType = "Primary",
                EffectiveFrom = today.AddDays(-10),
                Status = "Effective",
                Reason = "Initial location",
                RequestedByUserId = 1,
                RequestedAtUtc = DateTime.UtcNow,
                Version = 1
            });
            await setup.SaveChangesAsync();
        }

        async Task<bool> TryCreateRequest(long targetId)
        {
            await using var db = new RetailPOSDbContext(options);
            db.HrEmployeeLocationTransferRequests.Add(CreateTransfer(
                businessId, employeeId, targetId, today.AddDays(1)));
            try
            {
                await db.SaveChangesAsync();
                return true;
            }
            catch (DbUpdateException)
            {
                return false;
            }
        }

        var requestRace = await Task.WhenAll(TryCreateRequest(302), TryCreateRequest(303));
        Assert.Equal(1, requestRace.Count(result => result));
        Guid transferId;
        long approvalInstanceId;
        long targetId;
        await using (var update = new RetailPOSDbContext(options))
        {
            var request = await update.HrEmployeeLocationTransferRequests.SingleAsync(row =>
                row.BusinessId == businessId && row.EmployeeId == employeeId);
            transferId = request.Id;
            targetId = request.TargetLocationProjectionId;
            var policy = new ApprovalPolicy
            {
                BusinessId = businessId,
                Code = "EMPLOYEE_TRANSFER_PG_TEST",
                ProcessType = ApprovalProcessType.EmployeeTransfer,
                Name = "Test-scoped employee transfer approval",
                VersionNo = 1,
                ScopeType = ApprovalScopeType.Location,
                ScopeReference = targetId.ToString(),
                AllowSelfApproval = false,
                EffectiveFrom = today.AddDays(-1),
                IsActive = true
            };
            policy.Steps.Add(new ApprovalPolicyStep
            {
                Sequence = 1,
                ApproverType = ApprovalApproverType.User,
                ApproverReference = "test-approver",
                RequiredApprovals = 1,
                CanReject = true,
                CanCancel = true
            });
            update.ApprovalPolicies.Add(policy);
            await update.SaveChangesAsync();
            var instance = new ApprovalInstance
            {
                BusinessId = businessId,
                PolicyId = policy.Id,
                PolicyCodeSnapshot = policy.Code,
                PolicyVersionSnapshot = policy.VersionNo,
                AllowSelfApprovalSnapshot = false,
                ProcessType = ApprovalProcessType.EmployeeTransfer,
                TargetType = HrEmployeeLocationTransferConstants.TargetType,
                TargetId = transferId.ToString("D"),
                RequestedByUserId = 98573,
                RequestedAtUtc = DateTime.UtcNow,
                Status = ApprovalInstanceStatus.Pending,
                CurrentStepSequence = 1,
                CorrelationId = Guid.NewGuid(),
                ScopeType = ApprovalScopeType.Location,
                ScopeReference = targetId.ToString(),
                TargetVersion = 1,
                RequestHash = request.RequestHash
            };
            instance.Steps.Add(new ApprovalInstanceStep
            {
                Sequence = 1,
                ResolvedApproverType = ApprovalApproverType.User,
                ResolvedApproverReference = "test-approver",
                ResolvedApproverCandidatesJson = "[98572]",
                Decision = ApprovalStepDecision.Pending,
                RequiredApprovalsSnapshot = 1,
                PolicyStepVersionSnapshot = 1,
                CanRejectSnapshot = true,
                CanCancelSnapshot = true
            });
            update.ApprovalInstances.Add(instance);
            await update.SaveChangesAsync();
            approvalInstanceId = instance.Id;
            request.ApprovalInstanceId = approvalInstanceId;
            await update.SaveChangesAsync();
        }

        async Task<bool> TryApproveAsync(string decisionKey)
        {
            var auditContext = new AuditContext();
            auditContext.Initialize(new AuditContextSnapshot(
                Guid.NewGuid(), 98572, 98572, null, null, null, businessId, AuditSource.API,
                "POST", "/api/hr/v1/approvals/decisions", null, null, null, false,
                null, null, null, null, "PostgreSQL integration approver", "HR"));
            await using var db = new RetailPOSDbContext(new DbContextOptionsBuilder<RetailPOSDbContext>()
                .UseNpgsql(database.ConnectionString)
                .AddInterceptors(new AuditChangeTrackerInterceptor(
                    auditContext, NullLogger<AuditChangeTrackerInterceptor>.Instance))
                .Options);
            var auditService = new AuditService(
                db, auditContext, Mock.Of<IServiceProvider>(), NullLogger<AuditService>.Instance);
            var audit = new RequiredAuditTransaction(
                db, auditContext, auditService, NullLogger<RequiredAuditTransaction>.Instance);
            var handler = new ApprovalKernelHandlers(
                new ApprovalKernelStore(db),
                new TestApprovalIdentity(businessId, 98572),
                new TestApprovalAccess(),
                new TestApprovalCandidates(),
                new HrEmployeeLocationApprovalTargetSnapshotReader(db),
                new HrEmployeeLocationApprovalWorkflow(db),
                audit);
            try
            {
                var result = await handler.Handle(new RecordApprovalDecisionCommand(
                    approvalInstanceId, decisionKey, ApprovalDecisionType.Approve, "PostgreSQL race"),
                    CancellationToken.None);
                return result.Status == ApprovalInstanceStatus.Approved;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        var approvalRace = await Task.WhenAll(
            TryApproveAsync("pg-approval-race-1"),
            TryApproveAsync(sameDecisionKey ? "pg-approval-race-1" : "pg-approval-race-2"));
        Assert.Equal(sameDecisionKey ? 2 : 1, approvalRace.Count(approved => approved));
        await using (var statusCheck = new RetailPOSDbContext(options))
        {
            Assert.Equal(1, await statusCheck.ApprovalDecisions.CountAsync(row =>
                row.BusinessId == businessId && row.ApprovalInstanceId == approvalInstanceId));
            Assert.Equal(1, await statusCheck.AuditEvents.CountAsync(row =>
                row.BusinessId == businessId && row.ActionType == "hr.approval.decided"));
            var request = await statusCheck.HrEmployeeLocationTransferRequests
                .SingleAsync(row => row.Id == transferId);
            Assert.Equal("ApprovedPendingEffective", request.Status);
        }

        async Task<bool> TryActivateAsync()
        {
            var auditContext = new AuditContext();
            auditContext.Initialize(new AuditContextSnapshot(
                Guid.NewGuid(), null, null, null, null, null, businessId, AuditSource.SystemJob,
                null, "job:hr-employee-location-transfer-postgres-test", null, null, null, false,
                null, null, null, null, "PostgreSQL integration test", null));
            await using var db = new RetailPOSDbContext(new DbContextOptionsBuilder<RetailPOSDbContext>()
                .UseNpgsql(database.ConnectionString)
                .AddInterceptors(new AuditChangeTrackerInterceptor(
                    auditContext, NullLogger<AuditChangeTrackerInterceptor>.Instance))
                .Options);
            var auditService = new AuditService(
                db, auditContext, Mock.Of<IServiceProvider>(), NullLogger<AuditService>.Instance);
            var audit = new RequiredAuditTransaction(
                db, auditContext, auditService, NullLogger<RequiredAuditTransaction>.Instance);
            var store = new HrEmployeeLocationAssignmentStore(db);
            var activation = await audit.ExecuteAsync(
                new RequiredAuditOperation(
                    businessId,
                    "hr.employee_location_transfer.processed",
                    "HR",
                    "Processed a due employee location transfer.",
                    "HrEmployeeLocationTransfer",
                    transferId.ToString("D")),
                token => store.ActivateDueTransferAsync(
                    businessId, transferId, today.AddDays(1), token),
                result => result.Changed,
                CancellationToken.None);
            return activation.Changed;
        }

        var activationRace = await Task.WhenAll(TryActivateAsync(), TryActivateAsync());
        Assert.Equal(1, activationRace.Count(changed => changed));
        await using var verify = new RetailPOSDbContext(options);
        var employeeAfter = await verify.HrEmployees.AsNoTracking().SingleAsync(row => row.Id == employeeId);
        var transferAfter = await verify.HrEmployeeLocationTransferRequests.AsNoTracking()
            .SingleAsync(row => row.Id == transferId);
        Assert.Equal(targetId, employeeAfter.CurrentPrimaryLocationProjectionId);
        Assert.Equal("Effective", transferAfter.Status);
        Assert.Equal(2, await verify.HrEmployeeLocationAssignments.CountAsync(row =>
            row.BusinessId == businessId && row.EmployeeId == employeeId));
        Assert.Equal(1, await verify.HrEmployeeLocationAssignments.CountAsync(row =>
            row.BusinessId == businessId && row.EmployeeId == employeeId && row.Status == "Effective"));
        Assert.Equal(1, await verify.AuditEvents.CountAsync(row =>
            row.ActionType == "hr.employee_location_transfer.processed"));
    }

    private static OperationalLocationProjection CreateLocation(
        long businessId, long id, string sourceId, string name) => new()
    {
        Id = id,
        BusinessId = businessId,
        SourceSystem = "POS",
        SourceEntityType = "Outlet",
        SourceEntityId = sourceId,
        Name = name,
        SourceVersion = 1,
        SourcePayloadHash = "transfer-" + sourceId,
        SourceUpdatedAtUtc = DateTime.UtcNow,
        LastSyncedAtUtc = DateTime.UtcNow,
        IsSourceActive = true,
        SyncStatus = "Current"
    };

    private static HrEmployeeLocationTransferRequest CreateTransfer(
        long businessId, long employeeId, long targetId, DateOnly effectiveFrom) => new()
    {
        Id = Guid.NewGuid(),
        BusinessId = businessId,
        EmployeeId = employeeId,
        TargetLocationProjectionId = targetId,
        LocationNameSnapshot = "Target snapshot",
        SourceSystemSnapshot = "POS",
        SourceEntityTypeSnapshot = "Outlet",
        SourceEntityIdSnapshot = targetId.ToString(),
        LocationSourceVersionSnapshot = 1,
        EffectiveFrom = effectiveFrom,
        ExpectedEmployeeVersion = 1,
        Reason = "Concurrent transfer request",
        RequestHash = Guid.NewGuid().ToString("N").PadRight(64, '0'),
        Status = "PendingApproval",
        RequestedByUserId = 1,
        RequestedAtUtc = DateTime.UtcNow,
        Version = 1
    };

    private sealed class TestApprovalIdentity(long businessId, long userId) : IApprovalKernelIdentity
    {
        public long RequireBusinessId() => businessId;
        public long RequireRealUserId() => userId;
        public long? ActingRoleId => null;
        public Guid RequireCorrelationId() => Guid.NewGuid();
    }

    private sealed class TestApprovalAccess : IApprovalKernelAccess
    {
        public Task EnsureAsync(
            long businessId,
            ApprovalProcessType processType,
            ApprovalKernelOperation operation,
            ApprovalScopeType scopeType,
            string? scopeReference,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class TestApprovalCandidates : IApprovalCandidateResolver
    {
        public Task<IReadOnlyCollection<long>> ResolveAsync(
            long businessId,
            ApprovalPolicy policy,
            ApprovalPolicyStep step,
            long requesterUserId,
            ApprovalScopeType scopeType,
            string? scopeReference,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<long>>([98572]);
    }
}
