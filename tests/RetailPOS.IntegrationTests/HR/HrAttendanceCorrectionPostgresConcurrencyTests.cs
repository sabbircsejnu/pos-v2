using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.HR.Approvals;
using RetailPOS.Application.HR.Attendance;
using RetailPOS.Application.HR.Calendars;
using RetailPOS.Application.HR.Organisation;
using RetailPOS.Core.Audit;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Core.Entities.HR.Approvals;
using RetailPOS.Core.Entities.HR.Attendance;
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

public sealed class HrAttendanceCorrectionPostgresConcurrencyTests
{
    [Fact]
    public async Task ConcurrentCorrectionSubmissionsCreateOnlyOnePendingRequest()
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        var configuration = new ConfigurationBuilder().AddJsonFile(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "RetailPOS.API", "appsettings.json"))).Build();
        await using var database = await PostgresTestDatabase.CreateAsync(
            Environment.GetEnvironmentVariable("RETAILPOS_TEST_POSTGRES")
            ?? configuration.GetConnectionString("DefaultConnection")!, PostgresTestDatabasePrefix.MigrationBaseline);
        long dailyId;
        var workDate = new DateOnly(2026, 10, 10);

        await using (var db = CreateDb(database.ConnectionString))
        {
            Assert.False(db.Database.HasPendingModelChanges());
            await db.Database.MigrateAsync();
            await HrRosterPersistenceTests.Seed(db);
            var audit = CreateAudit(db, out var auditContext, 101);
            var clock = new CorrectionClock();
            var access = new HrCalendarPersistenceTests.Access();
            var calendars = new HrCalendarHandlers(access, new HrCalendarStore(db, clock), audit, clock);
            var shift = await calendars.Handle(new SaveHrShiftCommand(null, null,
                HrCalendarPersistenceTests.Shift()), CancellationToken.None);
            var rosters = new HrRosterHandlers(access, new HrRosterStore(db, new HrCalendarStore(db, clock), clock), audit);
            var roster = await rosters.Handle(new SaveHrRosterCommand(null, null,
                new(1, workDate, shift.Id, 10)), CancellationToken.None);
            await rosters.Handle(new TransitionHrRosterCommand(roster.Id, roster.Version, "Published"),
                CancellationToken.None);

            var punches = new HrRawAttendancePunchStore(db, clock);
            foreach (var (time, direction) in new[]
                     {
                         (new TimeOnly(9, 0), "IN"),
                         (new TimeOnly(17, 0), "OUT")
                     })
                await audit.ExecuteAsync(new RequiredAuditOperation(1, "hr.attendance.manual_punch_recorded",
                        "HR", "Recorded a manual punch.", "HrRawAttendancePunch", "manual"),
                    token => punches.AddManualAsync(1,
                        new ManualAttendancePunchInput(1, workDate.ToDateTime(time), "UTC", "Manual", direction),
                        new HrOperationalLocationReadScope(1, null), token), CancellationToken.None);

            var attendance = new HrAttendanceProcessingHandlers(access,
                new HrAttendanceProcessingStore(db, clock), audit);
            dailyId = Assert.Single(await attendance.Handle(
                new ProcessHrAttendanceCommand(workDate, workDate), CancellationToken.None)).Id;

            var policy = new ApprovalPolicy
            {
                BusinessId = 1,
                Code = "ATTENDANCE_CORRECTION",
                ProcessType = ApprovalProcessType.AttendanceCorrection,
                Name = "Attendance correction",
                VersionNo = 1,
                ScopeType = ApprovalScopeType.Location,
                ScopeReference = "10",
                EffectiveFrom = new DateOnly(2020, 1, 1),
                IsActive = true
            };
            policy.Steps.Add(new ApprovalPolicyStep
            {
                Sequence = 1,
                ApproverType = ApprovalApproverType.User,
                ApproverReference = "approver",
                RequiredApprovals = 1,
                CanReject = true,
                CanCancel = true
            });
            db.ApprovalPolicies.Add(policy);
            await db.SaveChangesAsync();
        }

        await using var first = new CorrectionSession(database.ConnectionString);
        await using var second = new CorrectionSession(database.ConnectionString);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<HrAttendanceCorrectionSubmissionResult?> Submit(CorrectionSession session)
        {
            await gate.Task;
            try
            {
                return await session.Handler.Handle(session.Request(Guid.NewGuid(), dailyId, workDate),
                    CancellationToken.None);
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        var firstSubmission = Submit(first);
        var secondSubmission = Submit(second);
        gate.SetResult();
        var submissions = await Task.WhenAll(firstSubmission, secondSubmission);
        Assert.Single(submissions, result => result is not null);
        Assert.Single(submissions, result => result is null);

        await using var verify = CreateDb(database.ConnectionString);
        Assert.Equal(1, await verify.Set<HrAttendanceCorrectionRequest>()
            .CountAsync(row => row.BusinessId == 1 && row.DailyAttendanceId == dailyId
                && row.Status == "PendingApproval"));
        Assert.Equal(1, await verify.ApprovalInstances.CountAsync(
            row => row.BusinessId == 1 && row.ProcessType == ApprovalProcessType.AttendanceCorrection));
    }

    private static RetailPOSDbContext CreateDb(string connectionString) =>
        new(new DbContextOptionsBuilder<RetailPOSDbContext>().UseNpgsql(connectionString).Options);

    private static IRequiredAuditTransaction CreateAudit(
        RetailPOSDbContext db, out AuditContext context, long userId)
    {
        context = new AuditContext();
        context.Initialize(new AuditContextSnapshot(Guid.NewGuid(), userId, userId, 5, 7, null, 1,
            AuditSource.API, "POST", "/api/hr/v1/attendance/corrections", null, null, null,
            false, null, null, null, null, "correction-test", "HR"));
        return new RequiredAuditTransaction(db, context,
            new AuditService(db, context, Mock.Of<IServiceProvider>(), NullLogger<AuditService>.Instance),
            NullLogger<RequiredAuditTransaction>.Instance);
    }

    private sealed class CorrectionSession : IAsyncDisposable
    {
        private readonly RetailPOSDbContext db;
        public HrAttendanceCorrectionHandlers Handler { get; }

        public CorrectionSession(string connectionString)
        {
            db = CreateDb(connectionString);
            var audit = CreateAudit(db, out _, 101);
            var clock = new CorrectionClock();
            var identity = new TestIdentity();
            var correctionWorkflow = new HrAttendanceCorrectionApprovalWorkflow(db, clock);
            var transferWorkflow = new HrEmployeeLocationApprovalWorkflow(db);
            var overtimeWorkflow = new HrOvertimeStore(db, clock);
            var targetReader = new HrApprovalTargetSnapshotReader(
                new HrEmployeeLocationApprovalTargetSnapshotReader(db), correctionWorkflow,
                new HrLeaveRequestStore(db, new HrCalendarStore(db, clock), new HrLeaveStore(db), clock),
                overtimeWorkflow);
            var approval = new ApprovalKernelHandlers(new ApprovalKernelStore(db), identity, new AllowApprovalAccess(),
                new CandidateResolver(), targetReader,
                new HrApprovalKernelWorkflow(transferWorkflow, correctionWorkflow,
                    new HrLeaveRequestStore(db, new HrCalendarStore(db, clock), new HrLeaveStore(db), clock),
                    overtimeWorkflow), audit);
            var sender = new Mock<MediatR.ISender>();
            sender.Setup(item => item.Send(It.IsAny<SubmitApprovalCommand>(), It.IsAny<CancellationToken>()))
                .Returns((SubmitApprovalCommand command, CancellationToken ct) => approval.Handle(command, ct));
            var organisationAccess = new Mock<IHrOrganisationAccess>();
            organisationAccess.Setup(access => access.RequireBusinessId()).Returns(1);
            organisationAccess.Setup(access => access.RequireRealUserId()).Returns(101);
            organisationAccess.Setup(access => access.EnsureAsync(1, HrOrganisationOperation.CorrectAttendance,
                    10, It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            Handler = new HrAttendanceCorrectionHandlers(organisationAccess.Object,
                new HrAttendanceCorrectionStore(db), sender.Object);
        }

        public RequestHrAttendanceCorrectionCommand Request(Guid correctionId, long dailyId, DateOnly workDate) =>
            new(correctionId, dailyId, 1,
                new HrAttendanceCorrectionValues(
                    DateTime.SpecifyKind(workDate.ToDateTime(new TimeOnly(9, 15)), DateTimeKind.Unspecified),
                    DateTime.SpecifyKind(workDate.ToDateTime(new TimeOnly(17, 0)), DateTimeKind.Unspecified),
                    465, 0, 10, 0, "Late"),
                "Concurrent correction test");

        public ValueTask DisposeAsync() => db.DisposeAsync();
    }

    private sealed class TestIdentity : IApprovalKernelIdentity
    {
        public long RequireBusinessId() => 1;
        public long RequireRealUserId() => 101;
        public long? ActingRoleId => 7;
        public Guid RequireCorrelationId() => Guid.NewGuid();
    }

    private sealed class AllowApprovalAccess : IApprovalKernelAccess
    {
        public Task EnsureAsync(long businessId, ApprovalProcessType processType,
            ApprovalKernelOperation operation, ApprovalScopeType scopeType, string? scopeReference,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class CandidateResolver : IApprovalCandidateResolver
    {
        public Task<IReadOnlyCollection<long>> ResolveAsync(long businessId, ApprovalPolicy policy,
            ApprovalPolicyStep step, long requesterUserId, ApprovalScopeType scopeType,
            string? scopeReference, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<long>>([202]);
    }

    private sealed class CorrectionClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 9, 0, 0, 0, TimeSpan.Zero);
    }
}
