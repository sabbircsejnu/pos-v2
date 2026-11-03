using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.HR.Approvals;
using RetailPOS.Application.HR.Attendance;
using RetailPOS.Application.HR.Calendars;
using RetailPOS.Application.HR.Leave;
using RetailPOS.Application.HR.Organisation;
using RetailPOS.Core.Audit;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Core.Entities.HR.Approvals;
using RetailPOS.Core.Entities.HR.Attendance;
using RetailPOS.Core.Entities.HR.Leave;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.AuditEvents;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.HR.Approvals;
using RetailPOS.Infrastructure.HR.Attendance;
using RetailPOS.Infrastructure.HR.Calendars;
using RetailPOS.Infrastructure.HR.Employees;
using RetailPOS.Infrastructure.HR.Leave;
using RetailPOS.Infrastructure.HR.Overtime;

namespace RetailPOS.IntegrationTests.HR;

public sealed class HrAttendanceCorrectionApprovalTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:;Foreign Keys=True");
    private readonly AuditContext _auditContext = new();
    private readonly FailAuditInterceptor _failure = new();
    private readonly CorrectionClock _clock = new();
    private RetailPOSDbContext _db = null!;
    private IRequiredAuditTransaction _audit = null!;
    private HrAttendanceProcessingHandlers _attendance = null!;
    private HrRawAttendancePunchStore _punches = null!;
    private HrAttendanceCorrectionHandlers _corrections = null!;
    private ApprovalKernelHandlers _approval = null!;
    private TestIdentity _identity = null!;
    private long _dailyId;
    private DateOnly WorkDate => DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime).AddDays(1);

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
        await HrRosterPersistenceTests.Seed(_db);
        _auditContext.Initialize(new AuditContextSnapshot(Guid.NewGuid(), 101, 101, 5, 7, null, 1,
            AuditSource.API, "POST", "/api/hr/v1/attendance/corrections", null, null, null,
            false, null, null, null, null, "requester", "HR"));
        _audit = new RequiredAuditTransaction(_db, _auditContext,
            new AuditService(_db, _auditContext, Mock.Of<IServiceProvider>(), NullLogger<AuditService>.Instance),
            NullLogger<RequiredAuditTransaction>.Instance);

        var access = new HrCalendarPersistenceTests.Access();
        var calendarStore = new HrCalendarStore(_db, _clock);
        var calendars = new HrCalendarHandlers(access, calendarStore, _audit, _clock);
        var shifts = await calendars.Handle(new SaveHrShiftCommand(null, null,
            HrCalendarPersistenceTests.Shift()), CancellationToken.None);
        var rosterStore = new HrRosterStore(_db, calendarStore, _clock);
        var rosters = new HrRosterHandlers(access, rosterStore, _audit);
        var roster = await rosters.Handle(new SaveHrRosterCommand(null, null,
            new(1, WorkDate, shifts.Id, 10)), CancellationToken.None);
        roster = await rosters.Handle(new TransitionHrRosterCommand(roster.Id, roster.Version, "Published"),
            CancellationToken.None);

        _punches = new HrRawAttendancePunchStore(_db, _clock);
        await AddPunch(WorkDate.ToDateTime(new TimeOnly(9, 0)), "IN");
        await AddPunch(WorkDate.ToDateTime(new TimeOnly(17, 0)), "OUT");
        _attendance = new HrAttendanceProcessingHandlers(access,
            new HrAttendanceProcessingStore(_db, _clock), _audit);
        _dailyId = Assert.Single(await _attendance.Handle(
            new ProcessHrAttendanceCommand(WorkDate, WorkDate), CancellationToken.None)).Id;

        _db.ApprovalPolicies.AddRange(
            CreatePolicy(ApprovalProcessType.AttendanceCorrection),
            CreatePolicy(ApprovalProcessType.Leave));
        await _db.SaveChangesAsync();
        _identity = new TestIdentity();
        var correctionWorkflow = new HrAttendanceCorrectionApprovalWorkflow(_db, _clock);
        var transferWorkflow = new HrEmployeeLocationApprovalWorkflow(_db);
        var overtimeWorkflow = new HrOvertimeStore(_db, _clock);
        var targetReader = new HrApprovalTargetSnapshotReader(
            new HrEmployeeLocationApprovalTargetSnapshotReader(_db), correctionWorkflow,
            new HrLeaveRequestStore(_db, calendarStore, new HrLeaveStore(_db), _clock),
            overtimeWorkflow);
        _approval = new ApprovalKernelHandlers(new ApprovalKernelStore(_db), _identity, new AllowAccess(),
            new CandidateResolver(), targetReader,
            new HrApprovalKernelWorkflow(transferWorkflow, correctionWorkflow,
                new HrLeaveRequestStore(_db, calendarStore, new HrLeaveStore(_db), _clock),
                overtimeWorkflow), _audit);

        var sender = new Mock<ISender>();
        sender.Setup(item => item.Send(It.IsAny<SubmitApprovalCommand>(), It.IsAny<CancellationToken>()))
            .Returns((SubmitApprovalCommand command, CancellationToken ct) => _approval.Handle(command, ct));
        _corrections = new HrAttendanceCorrectionHandlers(access, new HrAttendanceCorrectionStore(_db), sender.Object);
    }

    [Fact]
    public async Task ApprovedCorrectionIsAuditedIdempotentAndWinsOverDelayedPunches()
    {
        var correctionId = Guid.NewGuid();
        var values = CorrectedValues();
        var request = Request(correctionId, values);
        var auditCount = await _db.AuditEvents.CountAsync();
        var submitted = await _corrections.Handle(request, CancellationToken.None);
        Assert.Equal(ApprovalInstanceStatus.Pending, submitted.Status);
        Assert.False(submitted.IsReplay);
        Assert.Equal(auditCount + 1, await _db.AuditEvents.CountAsync());

        var duplicate = await _corrections.Handle(request, CancellationToken.None);
        Assert.True(duplicate.IsReplay);
        Assert.Equal(submitted.CorrectionId, duplicate.CorrectionId);
        Assert.Equal(submitted.ApprovalInstanceId, duplicate.ApprovalInstanceId);
        Assert.Equal(1, await _db.ApprovalInstances.CountAsync());

        await AddPunch(WorkDate.ToDateTime(new TimeOnly(10, 0)), "IN");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _attendance.Handle(
            new ProcessHrAttendanceCommand(WorkDate, WorkDate), CancellationToken.None));
        _db.ChangeTracker.Clear();
        Assert.Equal(1, (await _db.Set<HrDailyAttendance>().SingleAsync()).ProcessingVersion);
        var delayedPunch = await _db.Set<HrRawAttendancePunch>().OrderBy(row => row.Id).LastAsync();
        Assert.Equal("Pending", delayedPunch.ProcessingStatus);

        _identity.RealUserId = 202;
        var beforeDecisionAuditCount = await _db.AuditEvents.CountAsync();
        var approved = await _approval.Handle(new RecordApprovalDecisionCommand(
            submitted.ApprovalInstanceId, "approve-correction-once", ApprovalDecisionType.Approve, "Verified source"),
            CancellationToken.None);
        Assert.Equal(ApprovalInstanceStatus.Approved, approved.Status);
        Assert.Equal(beforeDecisionAuditCount + 1, await _db.AuditEvents.CountAsync());
        _db.ChangeTracker.Clear();
        var corrected = await _db.Set<HrDailyAttendance>().SingleAsync();
        Assert.Equal(values.FirstInAtLocal, corrected.FirstInAtLocal);
        Assert.Equal(values.LastOutAtLocal, corrected.LastOutAtLocal);
        Assert.Equal(values.WorkedMinutes, corrected.WorkedMinutes);
        Assert.True(corrected.IsManuallyAdjusted);
        Assert.Equal(2, corrected.ProcessingVersion);

        var replay = await _approval.Handle(new RecordApprovalDecisionCommand(
            submitted.ApprovalInstanceId, "approve-correction-once", ApprovalDecisionType.Approve, "Verified source"),
            CancellationToken.None);
        Assert.True(replay.IsReplay);

        var reprocessed = Assert.Single(await _attendance.Handle(
            new ProcessHrAttendanceCommand(WorkDate, WorkDate), CancellationToken.None));
        Assert.Equal(3, reprocessed.ProcessingVersion);
        Assert.Equal(values.FirstInAtLocal, reprocessed.FirstInAtLocal);
        Assert.Equal(values.WorkedMinutes, reprocessed.WorkedMinutes);
        Assert.Equal(3, reprocessed.PunchIds.Count);
        Assert.Equal(3, await _db.Set<HrRawAttendancePunch>().CountAsync(row => row.ProcessingStatus == "Processed"));

        var repeated = Assert.Single(await _attendance.Handle(
            new ProcessHrAttendanceCommand(WorkDate, WorkDate), CancellationToken.None));
        Assert.Equal(reprocessed.ProcessingVersion, repeated.ProcessingVersion);
        var history = Assert.Single(await _corrections.Handle(
            new GetHrAttendanceCorrectionsQuery(_dailyId), CancellationToken.None));
        Assert.Equal("Approved", history.Status);
        Assert.Contains("\"FirstInAtLocal\":\"2026-10-10T09:00:00", history.BeforeEvidenceJson);
        Assert.Contains("\"FirstInAtLocal\":\"2026-10-10T09:15:00", history.ProposedEvidenceJson);
        Assert.Contains("\"FirstInAtLocal\":\"2026-10-10T09:15:00", history.AppliedAfterEvidenceJson);
    }

    [Fact]
    public async Task LeaveApprovalConsumesLedgerOnceAndUpdatesRequestState()
    {
        var leaveStore = new HrLeaveStore(_db);
        var leaveType = await leaveStore.CreateTypeAsync(1,
            new HrLeaveTypeInput("ANNUAL", "Annual", true, false), CancellationToken.None);
        await leaveStore.CreatePolicyVersionAsync(1, new HrLeavePolicyInput(
            leaveType.Id, "STANDARD", "Standard", HrLeavePolicyScopeType.Business, null, 1,
            HrLeaveAccrualMethod.AnnualGrantAtLeaveYearStart, 12m, false, null, null,
            null, true, null, 1, 1, new DateOnly(2020, 1, 1), null, true), CancellationToken.None);

        var requestStore = new HrLeaveRequestStore(
            _db, new HrCalendarStore(_db, _clock), leaveStore, _clock);
        var request = await requestStore.CreateDraftAsync(
            1, 1, 101, new HrLeaveRequestDraftInput(leaveType.Id, WorkDate, WorkDate, "Annual leave"),
            CancellationToken.None);
        var snapshot = await requestStore.FindForApprovalAsync(1, request.Id, CancellationToken.None);
        var submitted = await _approval.Handle(new SubmitApprovalCommand(
            ApprovalProcessType.Leave, HrLeaveApprovalConstants.TargetType, request.Id.ToString(),
            ApprovalScopeType.Location, "10", request.Version, snapshot!.RequestHash!), CancellationToken.None);

        Assert.Equal(ApprovalInstanceStatus.Pending, submitted.Status);
        _identity.RealUserId = 202;
        var decisionKey = Guid.NewGuid().ToString("N");
        _failure.Fail = true;
        await Assert.ThrowsAsync<IOException>(() => _approval.Handle(new RecordApprovalDecisionCommand(
            submitted.InstanceId, decisionKey, ApprovalDecisionType.Approve, "Approved"), CancellationToken.None));
        _failure.Fail = false;
        _db.ChangeTracker.Clear();
        Assert.Equal("PendingApproval", (await requestStore.GetAsync(1, request.Id, CancellationToken.None))!.Status);
        Assert.Empty(await leaveStore.GetLedgerAsync(1, 1, leaveType.Id, CancellationToken.None));

        var approval = await _approval.Handle(new RecordApprovalDecisionCommand(
            submitted.InstanceId, decisionKey, ApprovalDecisionType.Approve, "Approved"), CancellationToken.None);
        var replay = await _approval.Handle(new RecordApprovalDecisionCommand(
            submitted.InstanceId, decisionKey, ApprovalDecisionType.Approve, "Approved"), CancellationToken.None);

        Assert.Equal(ApprovalInstanceStatus.Approved, approval.Status);
        Assert.True(replay.IsReplay);
        Assert.Equal("Approved", (await requestStore.GetAsync(1, request.Id, CancellationToken.None))!.Status);
        var ledger = await leaveStore.GetLedgerAsync(1, 1, leaveType.Id, CancellationToken.None);
        var consumption = Assert.Single(ledger);
        Assert.Equal(HrLeaveLedgerTransactionType.Consumption, consumption.TransactionType);
        Assert.Equal(-1m, consumption.Quantity);
    }

    [Fact]
    public async Task AuditFailureRollsBackApprovedCorrectionAndStaleVersionIsRejected()
    {
        var request = Request(Guid.NewGuid(), CorrectedValues());
        await Assert.ThrowsAsync<HrAttendanceCorrectionConcurrencyException>(() =>
            _corrections.Handle(request with { ExpectedProcessingVersion = 2 }, CancellationToken.None));
        Assert.Empty(await _db.Set<HrAttendanceCorrectionRequest>().ToListAsync());

        var submitted = await _corrections.Handle(request, CancellationToken.None);
        var auditCount = await _db.AuditEvents.CountAsync();
        _identity.RealUserId = 202;
        _failure.Fail = true;
        await Assert.ThrowsAsync<IOException>(() => _approval.Handle(new RecordApprovalDecisionCommand(
            submitted.ApprovalInstanceId, "approve-correction-retry", ApprovalDecisionType.Approve, null),
            CancellationToken.None));
        _db.ChangeTracker.Clear();

        var pending = await _db.Set<HrAttendanceCorrectionRequest>().SingleAsync();
        var original = await _db.Set<HrDailyAttendance>().SingleAsync();
        Assert.Equal("PendingApproval", pending.Status);
        Assert.Null(pending.AppliedAfterEvidenceJson);
        Assert.False(original.IsManuallyAdjusted);
        Assert.Equal(1, original.ProcessingVersion);
        Assert.Equal(auditCount, await _db.AuditEvents.CountAsync());

        _failure.Fail = false;
        var approved = await _approval.Handle(new RecordApprovalDecisionCommand(
            submitted.ApprovalInstanceId, "approve-correction-retry", ApprovalDecisionType.Approve, null),
            CancellationToken.None);
        Assert.Equal(ApprovalInstanceStatus.Approved, approved.Status);
        Assert.True(await _db.Set<HrAttendanceCorrectionRequest>().AnyAsync(row =>
            row.Status == "Approved" && row.AppliedAfterEvidenceJson != null));
    }

    [Fact]
    public async Task RejectionRetainsCorrectionEvidenceWithoutChangingAttendance()
    {
        var dailyBefore = await _db.Set<HrDailyAttendance>().SingleAsync();
        var originalFirstIn = dailyBefore.FirstInAtLocal;
        var submission = await _corrections.Handle(
            Request(Guid.NewGuid(), CorrectedValues()), CancellationToken.None);
        _identity.RealUserId = 202;

        var rejected = await _approval.Handle(new RecordApprovalDecisionCommand(
            submission.ApprovalInstanceId, "reject-correction", ApprovalDecisionType.Reject,
            "Evidence does not support the requested change."), CancellationToken.None);

        Assert.Equal(ApprovalInstanceStatus.Rejected, rejected.Status);
        _db.ChangeTracker.Clear();
        var dailyAfter = await _db.Set<HrDailyAttendance>().SingleAsync();
        var retained = await _db.Set<HrAttendanceCorrectionRequest>().SingleAsync();
        Assert.Equal(originalFirstIn, dailyAfter.FirstInAtLocal);
        Assert.False(dailyAfter.IsManuallyAdjusted);
        Assert.Equal(1, dailyAfter.ProcessingVersion);
        Assert.Equal("Rejected", retained.Status);
        Assert.NotEmpty(retained.BeforeEvidenceJson);
        Assert.NotEmpty(retained.ProposedEvidenceJson);
        Assert.Null(retained.AppliedAfterEvidenceJson);
        Assert.Equal("Evidence does not support the requested change.", retained.DecisionReason);
    }

    [Fact]
    public async Task CancellationRetainsCorrectionEvidenceWithoutChangingAttendance()
    {
        var submission = await _corrections.Handle(
            Request(Guid.NewGuid(), CorrectedValues()), CancellationToken.None);

        var cancelled = await _approval.Handle(new CancelApprovalCommand(
            submission.ApprovalInstanceId, "cancel-correction", "Submitted in error."), CancellationToken.None);

        Assert.Equal(ApprovalInstanceStatus.Cancelled, cancelled.Status);
        _db.ChangeTracker.Clear();
        var daily = await _db.Set<HrDailyAttendance>().SingleAsync();
        var retained = await _db.Set<HrAttendanceCorrectionRequest>().SingleAsync();
        Assert.False(daily.IsManuallyAdjusted);
        Assert.Equal(1, daily.ProcessingVersion);
        Assert.Equal("Cancelled", retained.Status);
        Assert.NotEmpty(retained.BeforeEvidenceJson);
        Assert.NotEmpty(retained.ProposedEvidenceJson);
        Assert.Null(retained.AppliedAfterEvidenceJson);
        Assert.Equal("Submitted in error.", retained.DecisionReason);
    }

    [Fact]
    public async Task CrossMidnightRosterPunchProcessingCorrectionAndReprocessRemainOneShiftStartRow()
    {
        var workDate = WorkDate.AddDays(1);
        var access = new HrCalendarPersistenceTests.Access();
        var calendarStore = new HrCalendarStore(_db, _clock);
        var calendars = new HrCalendarHandlers(access, calendarStore, _audit, _clock);
        var shift = await calendars.Handle(new SaveHrShiftCommand(null, null,
            HrCalendarPersistenceTests.Shift() with
            {
                Code = "NIGHT",
                Name = "Extended night",
                StartTimeLocal = new(10, 0),
                EndTimeLocal = new(2, 0),
                CrossesMidnight = true,
                MinimumWorkMinutes = 900,
                MinimumRestBeforeNextShiftMinutes = 0
            }), CancellationToken.None);
        var rosters = new HrRosterHandlers(access, new HrRosterStore(_db, calendarStore, _clock), _audit);
        var roster = await rosters.Handle(new SaveHrRosterCommand(null, null,
            new(1, workDate, shift.Id, 10)), CancellationToken.None);
        await rosters.Handle(new TransitionHrRosterCommand(roster.Id, roster.Version, "Published"),
            CancellationToken.None);

        await AddPunch(workDate.ToDateTime(new TimeOnly(10, 0)), "IN");
        await AddPunch(workDate.AddDays(1).ToDateTime(new TimeOnly(2, 0)), "OUT");
        var processor = new HrAttendanceProcessingHandlers(access,
            new HrAttendanceProcessingStore(_db, _clock), _audit);
        var processed = Assert.Single(await processor.Handle(
            new ProcessHrAttendanceCommand(workDate, workDate), CancellationToken.None));
        var initialProcessingVersion = processed.ProcessingVersion;
        Assert.Equal(workDate, processed.WorkDate);
        Assert.Equal(workDate.ToDateTime(new TimeOnly(10, 0)), processed.FirstInAtLocal);
        Assert.Equal(workDate.AddDays(1).ToDateTime(new TimeOnly(2, 0)), processed.LastOutAtLocal);
        Assert.Equal(960, processed.WorkedMinutes);
        Assert.Equal(1, await _db.Set<HrDailyAttendance>().CountAsync(row =>
            row.BusinessId == 1 && row.EmployeeId == 1 && row.WorkDate == workDate));
        Assert.False(await _db.Set<HrDailyAttendance>().AnyAsync(row =>
            row.BusinessId == 1 && row.EmployeeId == 1 && row.WorkDate == workDate.AddDays(1)));

        var correctionValues = new HrAttendanceCorrectionValues(
            workDate.ToDateTime(new TimeOnly(10, 15)),
            workDate.AddDays(1).ToDateTime(new TimeOnly(1, 45)),
            930, 0, 0, 15, "Present");
        var submission = await _corrections.Handle(new RequestHrAttendanceCorrectionCommand(
            Guid.NewGuid(), processed.Id, processed.ProcessingVersion, correctionValues,
            "Cross-midnight correction verified against signed source."), CancellationToken.None);
        _identity.RealUserId = 202;
        await _approval.Handle(new RecordApprovalDecisionCommand(
            submission.ApprovalInstanceId, "approve-cross-midnight-correction",
            ApprovalDecisionType.Approve, "Verified"), CancellationToken.None);
        _db.ChangeTracker.Clear();
        Assert.Equal(initialProcessingVersion + 1, (await _db.Set<HrDailyAttendance>()
            .SingleAsync(row => row.Id == processed.Id)).ProcessingVersion);

        await AddPunch(workDate.AddDays(1).ToDateTime(new TimeOnly(1, 55)), "OUT");
        var reprocessed = Assert.Single(await processor.Handle(
            new ProcessHrAttendanceCommand(workDate, workDate), CancellationToken.None));
        Assert.Equal(initialProcessingVersion + 2, reprocessed.ProcessingVersion);
        Assert.Equal(workDate, reprocessed.WorkDate);
        Assert.Equal(correctionValues.FirstInAtLocal, reprocessed.FirstInAtLocal);
        Assert.Equal(correctionValues.LastOutAtLocal, reprocessed.LastOutAtLocal);
        Assert.Equal(3, reprocessed.PunchIds.Count);
        Assert.Equal(3, await _db.Set<HrRawAttendancePunch>()
            .CountAsync(row => row.ProcessingStatus == "Processed"
                && row.PunchTimeLocal >= workDate.ToDateTime(TimeOnly.MinValue)));
        var repeated = Assert.Single(await processor.Handle(
            new ProcessHrAttendanceCommand(workDate, workDate), CancellationToken.None));
        Assert.Equal(reprocessed.ProcessingVersion, repeated.ProcessingVersion);
        Assert.Equal(1, await _db.Set<HrDailyAttendance>().CountAsync(row =>
            row.BusinessId == 1 && row.EmployeeId == 1 && row.WorkDate == workDate));

        var reportStore = new HrAttendanceReportStore(_db);
        var report = await reportStore.QueryAsync(1,
            new HrAttendanceReportQuery(workDate, workDate, EmployeeId: 1),
            new HrOperationalLocationReadScope(1, null), CancellationToken.None);
        var reportRow = Assert.Single(report.Items);
        Assert.Equal(workDate, reportRow.WorkDate);
        Assert.True(reportRow.ShiftCrossesMidnight);
        Assert.Equal(new TimeOnly(10, 0), reportRow.ShiftStartTimeLocal);
        Assert.Equal(new TimeOnly(2, 0), reportRow.ShiftEndTimeLocal);
        Assert.Equal(correctionValues.LastOutAtLocal, reportRow.LastOutAtLocal);
        Assert.True(reportRow.IsCorrected);
        Assert.Equal(reprocessed.ProcessingVersion, reportRow.ProcessingVersion);
        Assert.Equal(1, reportRow.CorrectionCount);
        Assert.Equal("Approved", reportRow.LatestCorrectionStatus);
        Assert.Equal(3, reportRow.Punches.Count);
        Assert.Equal(1, report.TotalCount);
        Assert.Equal(1, report.EarlyLeaveCount);
        Assert.Equal(1, report.ExceptionCount);
    }

    [Fact]
    public async Task AttendanceReportIsBusinessAndIdentityLocationScopedAndFiltersExceptions()
    {
        var daily = await _db.Set<HrDailyAttendance>().SingleAsync(row => row.Id == _dailyId);
        daily.Status = "Late";
        daily.LateMinutes = 10;
        daily.EarlyLeaveMinutes = 15;
        daily.DayClassification = "Holiday";
        daily.CalendarEvidenceJson = System.Text.Json.JsonSerializer.Serialize(
            new HrDayClassificationResponse(WorkDate, "Holiday", "Holiday", true, true,
                [new HrHolidayResponse(7, 8,
                    new HrHolidayData(WorkDate, "H", "Holiday", "Public", true, null), true, 1)],
                new HrWeeklyOffResponse(9, 1, 1,
                    new HrWeeklyOffData(new HrCalendarScope("Location", null, 10),
                        WorkDate, null, [WorkDate.DayOfWeek]), true, 1)));
        await _db.SaveChangesAsync();

        var reportStore = new HrAttendanceReportStore(_db);
        var report = await reportStore.QueryAsync(1,
            new HrAttendanceReportQuery(WorkDate, WorkDate, EmployeeId: 1, Status: "Late",
                ExceptionsOnly: true),
            new HrOperationalLocationReadScope(1, null), CancellationToken.None);
        Assert.Equal(1, report.TotalCount);
        Assert.Equal(1, report.LateCount);
        Assert.Equal(1, report.EarlyLeaveCount);
        Assert.Equal(0, report.AbsentCount);
        Assert.Equal(1, report.ExceptionCount);
        var row = Assert.Single(report.Items);
        Assert.Equal("EMP", row.EmployeeCode);
        Assert.Equal("Employee", row.EmployeeName);
        Assert.Equal("Holiday", row.DayClassification);
        Assert.Single(row.HolidayEvidence);
        Assert.NotNull(row.WeeklyOffEvidence);

        var outsideLocation = await reportStore.QueryAsync(1,
            new HrAttendanceReportQuery(WorkDate, WorkDate),
            new HrOperationalLocationReadScope(1, [new("Outlet", "not-authorized")]), CancellationToken.None);
        Assert.Empty(outsideLocation.Items);
        Assert.Equal(0, outsideLocation.TotalCount);

        var exceptions = await reportStore.QueryAsync(1,
            new HrAttendanceReportQuery(WorkDate, WorkDate, Status: "Absent", ExceptionsOnly: true),
            new HrOperationalLocationReadScope(1, null), CancellationToken.None);
        Assert.Empty(exceptions.Items);
        Assert.Equal(0, exceptions.TotalCount);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => reportStore.QueryAsync(2,
            new HrAttendanceReportQuery(WorkDate, WorkDate),
            new HrOperationalLocationReadScope(1, null), CancellationToken.None));
    }

    [Fact]
    public async Task CorrectionTargetCannotCrossBusinessOrChangeItsLocationScope()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            new HrAttendanceCorrectionStore(_db).GetLocationIdAsync(2, _dailyId, CancellationToken.None));

        var payload = new HrAttendanceCorrectionTargetPayload(Guid.NewGuid(), _dailyId, 1,
            CorrectedValues(), "Scope test");
        var json = System.Text.Json.JsonSerializer.Serialize(payload);
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(json)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _approval.Handle(new SubmitApprovalCommand(
            ApprovalProcessType.AttendanceCorrection, HrAttendanceCorrectionApprovalConstants.TargetType,
            payload.CorrectionId.ToString("D"), ApprovalScopeType.Location, "20", 1, hash, json),
            CancellationToken.None));
        Assert.Empty(await _db.Set<HrAttendanceCorrectionRequest>().ToListAsync());
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private RequestHrAttendanceCorrectionCommand Request(Guid id, HrAttendanceCorrectionValues values) =>
        new(id, _dailyId, 1, values, "Corrected using signed source record");

    private HrAttendanceCorrectionValues CorrectedValues() => new(
        WorkDate.ToDateTime(new TimeOnly(9, 15)),
        WorkDate.ToDateTime(new TimeOnly(17, 0)),
        465, 0, 10, 0, "Late");

    private async Task AddPunch(DateTime time, string direction) =>
        await _audit.ExecuteAsync(new RequiredAuditOperation(1, "hr.attendance.manual_punch_recorded", "HR",
                "Recorded a manual punch.", "HrRawAttendancePunch", "manual"),
            token => _punches.AddManualAsync(1,
                new ManualAttendancePunchInput(1, time, "UTC", "Manual", direction),
                new HrOperationalLocationReadScope(1, null), token), CancellationToken.None);

    private static ApprovalPolicy CreatePolicy(ApprovalProcessType processType)
    {
        var policy = new ApprovalPolicy
        {
            BusinessId = 1,
            Code = processType == ApprovalProcessType.Leave ? "LEAVE" : "ATTENDANCE_CORRECTION",
            ProcessType = processType,
            Name = processType == ApprovalProcessType.Leave ? "Leave approval" : "Attendance correction",
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
        public Task EnsureAsync(long businessId, ApprovalProcessType processType,
            ApprovalKernelOperation operation, ApprovalScopeType scopeType, string? scopeReference,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class CandidateResolver : IApprovalCandidateResolver
    {
        public Task<IReadOnlyCollection<long>> ResolveAsync(long businessId, ApprovalPolicy policy,
            ApprovalPolicyStep step, long requesterUserId, ApprovalScopeType scopeType,
            string? scopeReference, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<long>>([202]);
    }

    private sealed class CorrectionClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 9, 0, 0, 0, TimeSpan.Zero);
    }

    private sealed class FailAuditInterceptor : SaveChangesInterceptor
    {
        public bool Fail { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Fail && eventData.Context?.ChangeTracker.Entries<AuditEvent>()
                    .Any(entry => entry.State == EntityState.Added) == true)
                throw new IOException("Injected correction Audit v2 persistence failure.");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
