using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.HR.Approvals;
using RetailPOS.Application.HR.Attendance;
using RetailPOS.Application.HR.Calendars;
using RetailPOS.Application.HR.Organisation;
using RetailPOS.Application.HR.Overtime;
using RetailPOS.Core.Audit;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Core.Entities.HR.Approvals;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.AuditEvents;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.HR.Approvals;
using RetailPOS.Infrastructure.HR.Attendance;
using RetailPOS.Infrastructure.HR.Calendars;
using RetailPOS.Infrastructure.HR.Employees;
using RetailPOS.Infrastructure.HR.Leave;
using RetailPOS.Infrastructure.HR.Overtime;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.IntegrationTests.HR;

public sealed class HrOvertimePostgresTests
{
    [Fact]
    public async Task ConcurrentPolicyVersionCreationHasOneAuditedWinner()
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        var configuration = new ConfigurationBuilder().AddJsonFile(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "RetailPOS.API", "appsettings.json"))).Build();
        await using var database = await PostgresTestDatabase.CreateAsync(
            Environment.GetEnvironmentVariable("RETAILPOS_TEST_POSTGRES")
            ?? configuration.GetConnectionString("DefaultConnection")!, PostgresTestDatabasePrefix.MigrationBaseline);
        await using (var setup = new RetailPOSDbContext(new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseNpgsql(database.ConnectionString).Options))
        {
            Assert.False(setup.Database.HasPendingModelChanges());
            await setup.Database.MigrateAsync();
            await HrRosterPersistenceTests.Seed(setup);
        }

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<bool> CreatePolicy()
        {
            await using var db = CreateDb(database.ConnectionString);
            var (audit, _) = CreateAudit(db);
            var store = new HrOvertimeStore(db, TimeProvider.System);
            await gate.Task;
            try
            {
                await audit.ExecuteAsync(new RequiredAuditOperation(1, "hr.overtime.policy.created",
                        "HR", "Created a concurrency test policy.", "HrOvertimePolicyVersion", "NIGHT:1"),
                    token => store.CreatePolicyAsync(1, Policy(), token), CancellationToken.None);
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        var first = CreatePolicy();
        var second = CreatePolicy();
        gate.SetResult();
        var results = await Task.WhenAll(first, second);

        await using var verify = new RetailPOSDbContext(new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseNpgsql(database.ConnectionString).Options);
        Assert.Single(results, created => created);
        Assert.Single(results, created => !created);
        Assert.Single(await verify.Set<RetailPOS.Core.Entities.HR.Overtime.HrOvertimePolicyVersion>()
            .Where(row => row.BusinessId == 1 && row.Code == "NIGHT").ToListAsync());
        Assert.Single(await verify.AuditEvents
            .Where(row => row.PrimaryEntityType == "HrOvertimePolicyVersion").ToListAsync());
    }

    [Fact]
    public async Task EntryUniquenessAndApprovalLifecycleAreEnforcedByPostgres()
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        var configuration = new ConfigurationBuilder().AddJsonFile(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "RetailPOS.API", "appsettings.json"))).Build();
        await using var database = await PostgresTestDatabase.CreateAsync(
            Environment.GetEnvironmentVariable("RETAILPOS_TEST_POSTGRES")
            ?? configuration.GetConnectionString("DefaultConnection")!, PostgresTestDatabasePrefix.MigrationBaseline);
        await using var db = CreateDb(database.ConnectionString);
        await db.Database.MigrateAsync();
        await HrRosterPersistenceTests.Seed(db);
        var (audit, _) = CreateAudit(db);
        var clock = new PostgresOvertimeClock();
        var access = new HrCalendarPersistenceTests.Access();
        var calendarStore = new HrCalendarStore(db, clock);
        var calendars = new HrCalendarHandlers(access, calendarStore, audit, clock);
        var workDate = new DateOnly(2026, 10, 10);
        var shift = await calendars.Handle(new SaveHrShiftCommand(null, null,
            HrCalendarPersistenceTests.Shift()), CancellationToken.None);
        var rosters = new HrRosterHandlers(access, new HrRosterStore(db, calendarStore, clock), audit);
        var roster = await rosters.Handle(new SaveHrRosterCommand(null, null,
            new(1, workDate, shift.Id, 10)), CancellationToken.None);
        await rosters.Handle(new TransitionHrRosterCommand(roster.Id, roster.Version, "Published"),
            CancellationToken.None);

        var locationScope = new HrOperationalLocationReadScope(1, null);
        var punchStore = new HrRawAttendancePunchStore(db, clock);
        foreach (var (time, direction) in new[] { (new TimeOnly(9, 0), "IN"), (new TimeOnly(17, 0), "OUT") })
            await audit.ExecuteAsync(new RequiredAuditOperation(1, "hr.attendance.manual_punch_recorded",
                    "HR", "Recorded a PostgreSQL overtime test punch.", "HrRawAttendancePunch", direction),
                token => punchStore.AddManualAsync(1,
                    new ManualAttendancePunchInput(1, workDate.ToDateTime(time), "UTC", "Manual", direction),
                    locationScope, token), CancellationToken.None);
        var attendanceHandlers = new HrAttendanceProcessingHandlers(access,
            new HrAttendanceProcessingStore(db, clock), audit);
        var dailyAttendance = Assert.Single(await attendanceHandlers.Handle(
            new ProcessHrAttendanceCommand(workDate, workDate), CancellationToken.None));

        var overtime = new HrOvertimeStore(db, clock);
        var policy = Policy() with { EffectiveFrom = workDate.AddDays(-1) };
        await audit.ExecuteAsync(new RequiredAuditOperation(1, "hr.overtime.policy.created", "HR",
                "Created a PostgreSQL overtime test policy.", "HrOvertimePolicyVersion", "NIGHT:1"),
            token => overtime.CreatePolicyAsync(1, policy, token), CancellationToken.None);
        var entry = Assert.Single(await audit.ExecuteAsync(new RequiredAuditOperation(1,
                "hr.overtime.calculated", "HR", "Calculated PostgreSQL overtime.",
                "HrOvertimeEntry", "overtime-test"),
            token => overtime.CalculateAsync(1, 101, workDate, workDate, null, null, locationScope, token),
            CancellationToken.None));
        Assert.Equal("Calculated", entry.Status);

        var persistedEntry = await db.Set<RetailPOS.Core.Entities.HR.Overtime.HrOvertimeEntry>()
            .SingleAsync(row => row.Id == entry.Id);
        db.Set<RetailPOS.Core.Entities.HR.Overtime.HrOvertimeEntry>().Add(new()
        {
            BusinessId = persistedEntry.BusinessId,
            EmployeeId = persistedEntry.EmployeeId,
            WorkDate = persistedEntry.WorkDate,
            DailyAttendanceId = persistedEntry.DailyAttendanceId,
            OperationalLocationProjectionId = persistedEntry.OperationalLocationProjectionId,
            DayCategory = persistedEntry.DayCategory,
            AttendanceProcessingVersion = persistedEntry.AttendanceProcessingVersion,
            PolicyVersionId = persistedEntry.PolicyVersionId,
            PolicyVersionNo = persistedEntry.PolicyVersionNo,
            EligibleMinutes = persistedEntry.EligibleMinutes,
            CalculatedMinutes = persistedEntry.CalculatedMinutes,
            Status = persistedEntry.Status,
            CalculationVersion = 1,
            CalculationFingerprint = persistedEntry.CalculationFingerprint,
            RequestHash = persistedEntry.RequestHash,
            PolicySnapshotJson = persistedEntry.PolicySnapshotJson,
            AttendanceSnapshotJson = persistedEntry.AttendanceSnapshotJson,
            CalculationInputsJson = persistedEntry.CalculationInputsJson,
            Version = 1
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        Assert.Single(await db.Set<RetailPOS.Core.Entities.HR.Overtime.HrOvertimeEntry>()
            .Where(row => row.BusinessId == 1 && row.DailyAttendanceId == dailyAttendance.Id).ToListAsync());

        var approvalPolicy = new ApprovalPolicy
        {
            BusinessId = 1,
            Code = "OVERTIME",
            ProcessType = ApprovalProcessType.Overtime,
            Name = "Overtime approval",
            VersionNo = 1,
            ScopeType = ApprovalScopeType.Location,
            ScopeReference = "10",
            EffectiveFrom = workDate.AddYears(-1),
            IsActive = true
        };
        approvalPolicy.Steps.Add(new ApprovalPolicyStep
        {
            Sequence = 1,
            ApproverType = ApprovalApproverType.User,
            ApproverReference = "overtime-approver",
            RequiredApprovals = 1,
            CanReject = true,
            CanCancel = true
        });
        db.ApprovalPolicies.Add(approvalPolicy);
        db.Users.Add(new RetailPOS.Core.Entities.User
        {
            Id = 202,
            BusinessId = 1,
            Name = "Overtime approver",
            Email = "overtime-approver@example.invalid",
            PasswordHash = "test",
            IsActive = true
        });
        await db.SaveChangesAsync();

        var corrections = new HrAttendanceCorrectionApprovalWorkflow(db, clock);
        var transfers = new HrEmployeeLocationApprovalWorkflow(db);
        var leaveRequests = new HrLeaveRequestStore(db, calendarStore, new HrLeaveStore(db), clock);
        var approvalWorkflow = new HrApprovalKernelWorkflow(transfers, corrections, leaveRequests, overtime);
        var approvalTargets = new HrApprovalTargetSnapshotReader(
            new HrEmployeeLocationApprovalTargetSnapshotReader(db), corrections, leaveRequests, overtime);
        var approval = new ApprovalKernelHandlers(new ApprovalKernelStore(db), new ApprovalIdentity(101),
            new AllowApprovalAccess(), new OvertimeCandidateResolver(), approvalTargets,
            approvalWorkflow, audit);
        var submitted = await approval.Handle(new SubmitApprovalCommand(
            ApprovalProcessType.Overtime,
            HrOvertimeApprovalConstants.TargetType,
            entry.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ApprovalScopeType.Location,
            "10",
            entry.Version,
            persistedEntry.RequestHash), CancellationToken.None);
        Assert.Equal(ApprovalInstanceStatus.Pending, submitted.Status);

        var (approverAudit, _) = CreateAudit(db, 202);
        var approver = new ApprovalKernelHandlers(new ApprovalKernelStore(db), new ApprovalIdentity(202),
            new AllowApprovalAccess(), new OvertimeCandidateResolver(), approvalTargets,
            approvalWorkflow, approverAudit);
        var decided = await approver.Handle(new RecordApprovalDecisionCommand(
            submitted.InstanceId, "approve-overtime", ApprovalDecisionType.Approve, "Approved"),
            CancellationToken.None);
        Assert.Equal(ApprovalInstanceStatus.Approved, decided.Status);
        var approvedEntry = await db.Set<RetailPOS.Core.Entities.HR.Overtime.HrOvertimeEntry>()
            .SingleAsync(row => row.Id == entry.Id);
        Assert.Equal("PayrollEligible", approvedEntry.Status);
        Assert.Equal(approvedEntry.CalculatedMinutes, approvedEntry.ApprovedMinutes);
        Assert.Single(await db.Set<RetailPOS.Core.Entities.HR.Overtime.HrOvertimeCalculationEvidence>()
            .Where(row => row.OvertimeEntryId == entry.Id).ToListAsync());
    }

    private static RetailPOSDbContext CreateDb(string connection) =>
        new(new DbContextOptionsBuilder<RetailPOSDbContext>().UseNpgsql(connection).Options);

    private static (IRequiredAuditTransaction Audit, AuditContext Context) CreateAudit(
        RetailPOSDbContext db, long userId = 101)
    {
        var context = new AuditContext();
        context.Initialize(new AuditContextSnapshot(Guid.NewGuid(), userId, userId, 5, 7, null, 1,
            AuditSource.API, "POST", "/api/hr/v1/overtime/policies", null, null, null,
            false, null, null, null, null, "overtime tester", "HR"));
        var audit = new RequiredAuditTransaction(db, context,
            new AuditService(db, context, Mock.Of<IServiceProvider>(), NullLogger<AuditService>.Instance),
            NullLogger<RequiredAuditTransaction>.Instance);
        return (audit, context);
    }

    private static HrOvertimePolicyInput Policy() => new(
        "NIGHT", "Night shift overtime", RetailPOS.Core.Entities.HR.Overtime.HrOvertimePolicyScopeType.Business,
        null, 1, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1), null,
        15, 240, 15, RetailPOS.Core.Entities.HR.Overtime.HrOvertimeRoundingMethod.Down,
        false, true, true, true, true, false,
        RetailPOS.Core.Entities.HR.Overtime.HrOvertimeRateType.Multiplier,
        1.5m, 2m, 2m, 2m, true);

    private sealed class PostgresOvertimeClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 9, 0, 0, 0, TimeSpan.Zero);
    }

    private sealed class ApprovalIdentity(long userId) : IApprovalKernelIdentity
    {
        public long RequireBusinessId() => 1;
        public long RequireRealUserId() => userId;
        public long? ActingRoleId => 7;
        public Guid RequireCorrelationId() => Guid.NewGuid();
    }

    private sealed class AllowApprovalAccess : IApprovalKernelAccess
    {
        public Task EnsureAsync(long businessId, ApprovalProcessType processType,
            ApprovalKernelOperation operation, ApprovalScopeType scopeType, string? scopeReference,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class OvertimeCandidateResolver : IApprovalCandidateResolver
    {
        public Task<IReadOnlyCollection<long>> ResolveAsync(long businessId, ApprovalPolicy policy,
            ApprovalPolicyStep step, long requesterUserId, ApprovalScopeType scopeType,
            string? scopeReference, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<long>>([202]);
    }
}
