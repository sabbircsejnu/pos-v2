using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.HR.Attendance;
using RetailPOS.Application.HR.Calendars;
using RetailPOS.Application.HR.Overtime;
using RetailPOS.Core.Audit;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Core.Entities.HR.Overtime;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.AuditEvents;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.HR.Attendance;
using RetailPOS.Infrastructure.HR.Calendars;
using RetailPOS.Infrastructure.HR.Overtime;

namespace RetailPOS.IntegrationTests.HR;

public sealed class HrOvertimePersistenceTests : IAsyncLifetime
{
    private readonly SqliteConnection connection = new("Data Source=:memory:;Foreign Keys=True");
    private readonly AuditContext auditContext = new();
    private readonly OvertimeClock clock = new();
    private RetailPOSDbContext db = null!;
    private IRequiredAuditTransaction audit = null!;
    private HrOvertimeStore overtime = null!;
    private readonly DateOnly workDate = new(2026, 10, 10);

    public async Task InitializeAsync()
    {
        await connection.OpenAsync();
        db = new RetailPOSDbContext(new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(new AuditChangeTrackerInterceptor(
                auditContext, NullLogger<AuditChangeTrackerInterceptor>.Instance))
            .Options);
        await db.Database.EnsureCreatedAsync();
        await HrRosterPersistenceTests.Seed(db);
        auditContext.Initialize(new AuditContextSnapshot(Guid.NewGuid(), 101, 101, 5, 7, null, 1,
            AuditSource.API, "POST", "/api/hr/v1/overtime/calculate", null, null, null,
            false, null, null, null, null, "overtime tester", "HR"));
        audit = new RequiredAuditTransaction(db, auditContext,
            new AuditService(db, auditContext, Mock.Of<IServiceProvider>(),
                NullLogger<AuditService>.Instance),
            NullLogger<RequiredAuditTransaction>.Instance);

        var access = new HrCalendarPersistenceTests.Access();
        var calendarStore = new HrCalendarStore(db, clock);
        var calendars = new HrCalendarHandlers(access, calendarStore, audit, clock);
        var shift = await calendars.Handle(new SaveHrShiftCommand(null, null,
            new HrShiftData("NIGHT", "Night", new(10, 0), new(2, 0), true,
                900, 1020, 15, 15, 600, 0, 0, true, [new(1, 30, false)])), default);
        var rosters = new HrRosterHandlers(access, new HrRosterStore(db, calendarStore, clock), audit);
        var roster = await rosters.Handle(new SaveHrRosterCommand(null, null,
            new(1, workDate, shift.Id, 10)), default);
        await rosters.Handle(new TransitionHrRosterCommand(roster.Id, roster.Version, "Published"), default);

        var locationScope = new RetailPOS.Application.HR.Organisation.HrOperationalLocationReadScope(1, null);
        var rawPunchStore = new HrRawAttendancePunchStore(db, clock);
        await AddPunch(rawPunchStore, locationScope, workDate.ToDateTime(new(10, 0)), "IN");
        await AddPunch(rawPunchStore, locationScope, workDate.AddDays(1).ToDateTime(new(2, 5)), "OUT");
        var attendance = new HrAttendanceProcessingHandlers(access,
            new HrAttendanceProcessingStore(db, clock), audit);
        Assert.Single(await attendance.Handle(
            new ProcessHrAttendanceCommand(workDate, workDate), default));

        overtime = new HrOvertimeStore(db, clock);
        var policy = Policy();
        await audit.ExecuteAsync(new RequiredAuditOperation(1, "hr.overtime.policy.created", "HR",
                "Created overtime policy for integration test.", "HrOvertimePolicyVersion", "NIGHT:1"),
            token => overtime.CreatePolicyAsync(1, policy, token), default);
    }

    [Fact]
    public async Task CrossMidnightDelayedPunchRecalculatesOneCanonicalWorkDateIdempotently()
    {
        var scope = new RetailPOS.Application.HR.Organisation.HrOperationalLocationReadScope(1, null);
        var firstResult = await audit.ExecuteAsync(new RequiredAuditOperation(1,
                "hr.overtime.calculated", "HR", "Calculated overtime.", "HrOvertimeEntry", "range"),
            token => overtime.CalculateAsync(1, 101, workDate, workDate, null, null, scope, token), default);
        var first = Assert.Single(firstResult);
        Assert.Equal(workDate, first.WorkDate);
        Assert.Equal(5, first.CalculatedMinutes);
        Assert.Equal(1, first.CalculationVersion);
        Assert.Single(first.Evidence);
        Assert.Equal(1, await db.Set<RetailPOS.Core.Entities.HR.Attendance.HrDailyAttendance>()
            .CountAsync(row => row.BusinessId == 1 && row.EmployeeId == 1 && row.WorkDate == workDate));

        var rawPunches = new HrRawAttendancePunchStore(db, clock);
        await AddPunch(rawPunches, scope, workDate.AddDays(1).ToDateTime(new(2, 15)), "OUT");
        var attendance = new HrAttendanceProcessingHandlers(new HrCalendarPersistenceTests.Access(),
            new HrAttendanceProcessingStore(db, clock), audit);
        var reprocessed = Assert.Single(await attendance.Handle(
            new ProcessHrAttendanceCommand(workDate, workDate), default));
        Assert.Equal(2, reprocessed.ProcessingVersion);
        Assert.Equal(workDate.ToDateTime(new(10, 0)), reprocessed.FirstInAtLocal);
        Assert.Equal(workDate.AddDays(1).ToDateTime(new(2, 15)), reprocessed.LastOutAtLocal);

        var secondResult = await audit.ExecuteAsync(new RequiredAuditOperation(1,
                "hr.overtime.calculated", "HR", "Recalculated delayed overtime.",
                "HrOvertimeEntry", "range"),
            token => overtime.CalculateAsync(1, 101, workDate, workDate, null, null, scope, token), default);
        var second = Assert.Single(secondResult);
        Assert.Equal(15, second.CalculatedMinutes);
        Assert.Equal(2, second.CalculationVersion);
        Assert.Equal(2, second.AttendanceProcessingVersion);
        Assert.Equal([1, 2], second.Evidence.Select(item => item.CalculationVersion));

        var repeated = Assert.Single(await audit.ExecuteAsync(new RequiredAuditOperation(1,
                "hr.overtime.calculated", "HR", "Repeated overtime calculation.",
                "HrOvertimeEntry", "range"),
            token => overtime.CalculateAsync(1, 101, workDate, workDate, null, null, scope, token), default));
        Assert.Equal(second.Id, repeated.Id);
        Assert.Equal(second.CalculationVersion, repeated.CalculationVersion);
        Assert.Equal(2, repeated.Evidence.Count);
        Assert.Equal(0, await db.Set<RetailPOS.Core.Entities.HR.Attendance.HrDailyAttendance>()
            .CountAsync(row => row.BusinessId == 1 && row.EmployeeId == 1 && row.WorkDate == workDate.AddDays(1)));
    }

    private async Task AddPunch(
        HrRawAttendancePunchStore store,
        RetailPOS.Application.HR.Organisation.HrOperationalLocationReadScope scope,
        DateTime localTime,
        string direction)
    {
        var input = new ManualAttendancePunchInput(1, localTime, "UTC", "Manual", direction);
        await audit.ExecuteAsync(new RequiredAuditOperation(1, "hr.attendance.manual_punch_recorded",
                "HR", "Recorded overtime test punch.", "HrRawAttendancePunch", $"{localTime:O}"),
            token => store.AddManualAsync(1, input, scope, token), default);
    }

    private HrOvertimePolicyInput Policy() => new(
        "NIGHT", "Night shift overtime", HrOvertimePolicyScopeType.Business, null,
        1, workDate, null, 1, null, 1, HrOvertimeRoundingMethod.Down,
        false, true, true, true, true, false, HrOvertimeRateType.Multiplier,
        1.5m, 2m, 2m, 2m, true);

    public async Task DisposeAsync()
    {
        await db.DisposeAsync();
        await connection.DisposeAsync();
    }

    private sealed class OvertimeClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
    }
}
