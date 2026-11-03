using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.HR.Attendance;
using RetailPOS.Application.HR.Calendars;
using RetailPOS.Application.HR.Organisation;
using RetailPOS.Core.Audit;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Core.Entities.HR.Attendance;
using RetailPOS.Core.Entities.HR.Employees;
using RetailPOS.Core.Entities.HR.Organisation;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.AuditEvents;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.HR.Attendance;
using RetailPOS.Infrastructure.HR.Calendars;

namespace RetailPOS.IntegrationTests.HR;

public sealed class HrRosterPersistenceTests : IAsyncLifetime
{
    private readonly SqliteConnection connection = new("Data Source=:memory:;Foreign Keys=True");
    private RetailPOSDbContext db = null!;
    private HrRosterHandlers handlers = null!;
    private HrCalendarHandlers calendars = null!;
    private HrRosterStore store = null!;
    private HrRawAttendancePunchStore rawPunches = null!;
    private HrAttendanceProcessingStore attendanceStore = null!;
    private HrAttendanceProcessingHandlers attendanceHandlers = null!;
    private IRequiredAuditTransaction audit = null!;
    private readonly FailAudit failure = new();
    private readonly Clock clock = new();
    private DateOnly Day => DateOnly.FromDateTime(clock.Now.UtcDateTime).AddDays(1);
    private long shiftId;
    public async Task InitializeAsync()
    {
        await connection.OpenAsync();
        var context = new AuditContext();
        db = new(new DbContextOptionsBuilder<RetailPOSDbContext>().UseSqlite(connection)
            .AddInterceptors(new AuditChangeTrackerInterceptor(context, NullLogger<AuditChangeTrackerInterceptor>.Instance), failure).Options);
        await db.Database.EnsureCreatedAsync(); await Seed(db);
        context.Initialize(new AuditContextSnapshot(Guid.NewGuid(), 101, 101, 5, 7, null, 1, AuditSource.API,
            "POST", "/api/hr/v1/rosters", null, null, null, false, null, null, null, null, "actor", "HR"));
        audit = new RequiredAuditTransaction(db, context, new AuditService(db, context, Mock.Of<IServiceProvider>(),
            NullLogger<AuditService>.Instance), NullLogger<RequiredAuditTransaction>.Instance);
        var calendarStore = new HrCalendarStore(db, clock);
        calendars = new(new HrCalendarPersistenceTests.Access(), calendarStore, audit, clock);
        store = new(db, calendarStore, clock);
        rawPunches = new(db, clock);
        attendanceStore = new(db, clock);
        handlers = new(new HrCalendarPersistenceTests.Access(), store, audit);
        attendanceHandlers = new(new HrCalendarPersistenceTests.Access(), attendanceStore, audit);
        shiftId = (await calendars.Handle(new SaveHrShiftCommand(null, null, HrCalendarPersistenceTests.Shift()), default)).Id;
    }
    internal static async Task Seed(RetailPOSDbContext db, bool businessExists = false)
    {
        if (!businessExists) db.Businesses.AddRange(new Business { Id = 1, Name = "Roster" }, new Business { Id = 2, Name = "Other" });
        db.Outlets.Add(new Outlet { Id = 10, BusinessId = 1, Name = "Outlet" });
        db.OperationalLocationProjections.AddRange(
            new OperationalLocationProjection { Id = 10, BusinessId = 1, SourceSystem = "RetailPOS", SourceEntityType = "Outlet", SourceEntityId = "10", Name = "Outlet", SourceVersion = 1 },
            new OperationalLocationProjection { Id = 20, BusinessId = 2, SourceSystem = "RetailPOS", SourceEntityType = "Outlet", SourceEntityId = "20", Name = "Foreign", SourceVersion = 1 });
        db.HrEmploymentTypes.Add(new HrEmploymentType { Id = 1, BusinessId = 1, Code = "PERM", Name = "Permanent" });
        db.HrEmployees.Add(new HrEmployee { Id = 1, BusinessId = 1, EmployeeCode = "EMP", FirstName = "Employee",
            DisplayName = "Employee", CurrentEmploymentTypeId = 1, JoiningDate = new(2026, 1, 1),
            CurrentPrimaryLocationProjectionId = 10 });
        db.HrEmploymentPeriods.Add(new HrEmploymentPeriod { Id = 1, BusinessId = 1, EmployeeId = 1,
            EmploymentTypeId = 1, StartDate = new(2026, 1, 1) });
        db.HrEmployeeLocationAssignments.Add(new HrEmployeeLocationAssignment { BusinessId = 1, EmployeeId = 1,
            OperationalLocationProjectionId = 10, EffectiveFrom = new(2026, 1, 1), LocationNameSnapshot = "Outlet",
            SourceSystemSnapshot = "RetailPOS", SourceEntityTypeSnapshot = "Outlet", SourceEntityIdSnapshot = "10",
            LocationSourceVersionSnapshot = 1, RequestedByUserId = 101, RequestedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }
    private HrRosterData Data() => new(1, Day, shiftId, 10);
    [Fact]
    public async Task ManualPunchNormalizesTimezoneAndDuplicateReturnsSameImmutableEvidence()
    {
        var input = new ManualAttendancePunchInput(1, new(2026, 10, 10, 9, 0, 0),
            "India Standard Time", "Manual", "IN");
        var scope = new HrOperationalLocationReadScope(1, null);
        var operation = new RequiredAuditOperation(1, "hr.attendance.manual_punch_recorded", "HR",
            "Recorded a manual punch.", "HrRawAttendancePunch", "manual");
        var first = await audit.ExecuteAsync(operation, t => rawPunches.AddManualAsync(1, input, scope, t), default);
        var duplicateInput = input with { VerificationMethod = "Face", DirectionCode = "OUT" };
        var duplicate = await audit.ExecuteAsync(operation, t => rawPunches.AddManualAsync(1, duplicateInput, scope, t), default);
        Assert.False(first.Duplicate);
        Assert.True(duplicate.Duplicate);
        Assert.Equal(first.Id, duplicate.Id);
        Assert.Equal(new DateTimeOffset(2026, 10, 10, 3, 30, 0, TimeSpan.Zero), first.PunchTimeUtc);
        Assert.Equal("Manual", first.SourceType);
        Assert.Null(first.DeviceId);
        var row = await db.Set<HrRawAttendancePunch>().SingleAsync(p => p.Id == first.Id);
        row.DirectionCode = "OUT";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.Entry(row).Reload();
        row.ProcessingStatus = "Processed";
        row.ProcessingVersion++;
        await db.SaveChangesAsync();
        Assert.Equal("Processed", row.ProcessingStatus);
    }

    [Fact]
    public async Task NormalizedDevicePunchCanRemainUnmappedAndPreservesCrossMidnightEvidence()
    {
        db.Add(new HrAttendanceDevice { BusinessId = 1, DeviceCode = "INT-1", SerialNumber = "INT-SERIAL",
            VendorCode = "INTERNAL", Model = "Normalized", OperationalLocationProjectionId = 10,
            ProtocolType = "API", DeviceTimeZoneId = "UTC", Status = "Active" });
        await db.SaveChangesAsync();
        var beforeMidnight = await rawPunches.IngestDeviceAsync(new(1, "INT-SERIAL", "unmapped-user",
            new(2026, 10, 10, 23, 30, 0)), default);
        var afterMidnight = await rawPunches.IngestDeviceAsync(new(1, "INT-SERIAL", "unmapped-user",
            new(2026, 10, 11, 1, 15, 0)), default);
        Assert.Equal("Unmapped", beforeMidnight.ProcessingStatus);
        Assert.Null(beforeMidnight.EmployeeId);
        Assert.Equal("Device", beforeMidnight.SourceType);
        Assert.Equal(new DateTimeOffset(2026, 10, 10, 23, 30, 0, TimeSpan.Zero), beforeMidnight.PunchTimeUtc);
        Assert.Equal(new DateTime(2026, 10, 10, 23, 30, 0), beforeMidnight.PunchTimeLocal);
        Assert.Equal(new DateTime(2026, 10, 11, 1, 15, 0), afterMidnight.PunchTimeLocal);
        Assert.NotEqual(beforeMidnight.IdempotencyFingerprint, afterMidnight.IdempotencyFingerprint);
    }
    [Fact]
    public async Task StoreRechecksIdentityScopeInsideTransaction()
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.SaveAsync(1, null, null, Data(),
            new(1, [new("Outlet", "999")]), default));
        Assert.Empty(await db.Set<HrRosterAssignment>().ToListAsync());
        await tx.RollbackAsync();
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task DraftExcludedPublishedFreezesOneWorkDateAndPrecedence(bool midnight)
    {
        if (midnight) {
            var shift = await calendars.Handle(new SaveHrShiftCommand(shiftId, 1,
                HrCalendarPersistenceTests.Shift() with { StartTimeLocal = new(10, 0), EndTimeLocal = new(2, 0),
                    CrossesMidnight = true, MinimumWorkMinutes = 900, MinimumRestBeforeNextShiftMinutes = 0 }), default);
            Assert.True(shift.Data.CrossesMidnight);
        }
        var cal = await calendars.Handle(new SaveHrHolidayCalendarCommand(null, null, new("Holiday", new("Company"), Day, null)), default);
        await calendars.Handle(new SaveHrHolidayCommand(cal.Id, null, null, new(Day, "H", "Holiday", "Public", true, null)), default);
        await calendars.Handle(new SaveHrWeeklyOffCommand(null, null, new(new("Company"), Day, null, [Day.DayOfWeek])), default);
        var row = await handlers.Handle(new SaveHrRosterCommand(null, null, Data()), default);
        Assert.Null(await store.PublishedAsync(1, 1, Day, default));
        Assert.Null(row.PublishedShift);
        var published = await handlers.Handle(new TransitionHrRosterCommand(row.Id, row.Version, "Published"), default);
        var resolved = await handlers.Handle(new GetHrRosterClassificationQuery(1, Day), default);
        Assert.Equal("NormalWorkingDay", resolved.Classification); Assert.Equal("PublishedRoster", resolved.Source);
        Assert.True(resolved.IsHoliday); Assert.NotNull(resolved.WeeklyOffPolicy);
        Assert.Equal(Day, published.Data.WorkDate); Assert.Single(await db.Set<HrRosterAssignment>().ToListAsync());
        Assert.Equal(midnight, published.PublishedShift!.CrossesMidnight);
        await calendars.Handle(new SaveHrShiftCommand(shiftId, midnight ? 2 : 1, null), default);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(published.PublishedShift),
            System.Text.Json.JsonSerializer.Serialize((await store.PublishedAsync(1, 1, Day, default))!.PublishedShift));
    }
    [Fact]
    public async Task DuplicateAndStaleUpdatesConflict()
    {
        var row = await handlers.Handle(new SaveHrRosterCommand(null, null, Data()), default);
        await Assert.ThrowsAsync<HrCalendarConflictException>(() => handlers.Handle(new SaveHrRosterCommand(null, null, Data()), default));
        var update = await handlers.Handle(new SaveHrRosterCommand(row.Id, row.Version, Data() with { WorkKind = "Extended" }), default);
        await Assert.ThrowsAsync<HrCalendarConflictException>(() => handlers.Handle(new TransitionHrRosterCommand(row.Id, row.Version, "Published"), default));
        Assert.Equal(2, update.Version);
    }
    [Theory]
    [InlineData("inactive")] [InlineData("foreign-shift")] [InlineData("foreign-location")] [InlineData("inactive-location")]
    [InlineData("unowned-location")] [InlineData("outside-employment")] [InlineData("inactive-employee")] [InlineData("missing-assignment")]
    public async Task InvalidReferencesFailWithoutRosterOrAudit(string kind)
    {
        var input = Data();
        if (kind == "inactive") await calendars.Handle(new SaveHrShiftCommand(shiftId, 1, null), default);
        if (kind == "foreign-shift") input = input with { ShiftId = 999 };
        if (kind == "foreign-location") input = input with { OperationalLocationProjectionId = 20 };
        if (kind == "inactive-location") (await db.OperationalLocationProjections.FindAsync(10L))!.IsSourceActive = false;
        if (kind == "unowned-location") (await db.OperationalLocationProjections.FindAsync(10L))!.SourceEntityId = "999";
        if (kind == "outside-employment") (await db.HrEmploymentPeriods.FindAsync(1L))!.EndDate = Day.AddDays(-1);
        if (kind == "inactive-employee") (await db.HrEmployees.FindAsync(1L))!.CurrentEmploymentStatus = "Exited";
        if (kind == "missing-assignment") (await db.HrEmployeeLocationAssignments.SingleAsync()).EffectiveTo = Day.AddDays(-1);
        await db.SaveChangesAsync(); var auditCount = await db.AuditEvents.CountAsync();
        var exception = await Record.ExceptionAsync(() => handlers.Handle(new SaveHrRosterCommand(null, null, input), default));
        Assert.True(exception is ArgumentException or InvalidOperationException, exception?.ToString());
        Assert.Empty(await db.Set<HrRosterAssignment>().ToListAsync()); Assert.Equal(auditCount, await db.AuditEvents.CountAsync());
    }
    [Fact]
    public async Task AuditFailureRollsBackPublicationAndFrozenContext()
    {
        var row = await handlers.Handle(new SaveHrRosterCommand(null, null, Data()), default);
        var count = await db.AuditEvents.CountAsync(); failure.Fail = true;
        await Assert.ThrowsAsync<IOException>(() => handlers.Handle(new TransitionHrRosterCommand(row.Id, row.Version, "Published"), default));
        db.ChangeTracker.Clear(); var persisted = await db.Set<HrRosterAssignment>().SingleAsync();
        Assert.Equal("Draft", persisted.Status); Assert.Null(persisted.ShiftSnapshotJson);
        Assert.Equal(row.Version, persisted.Version); Assert.Equal(count, await db.AuditEvents.CountAsync());
    }
    [Fact]
    public async Task HistoricalPublishedRowCannotBeEditedCancelledOrDeleted()
    {
        var date = Day;
        var row = await handlers.Handle(new SaveHrRosterCommand(null, null, Data()), default);
        row = await handlers.Handle(new TransitionHrRosterCommand(row.Id, row.Version, "Published"), default);
        clock.Now = clock.Now.AddDays(2);
        await Assert.ThrowsAsync<HrCalendarConflictException>(() => handlers.Handle(new TransitionHrRosterCommand(row.Id, row.Version, "Cancelled"), default));
        await Assert.ThrowsAsync<HrCalendarConflictException>(() => handlers.Handle(new SaveHrRosterCommand(row.Id, row.Version, row.Data), default));
        Assert.NotNull(await store.PublishedAsync(1, 1, date, default));
        db.Remove(await db.Set<HrRosterAssignment>().SingleAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }
    [Fact]
    public async Task FutureCancellationRetainsSnapshotAndAllowsReplacement()
    {
        var row = await handlers.Handle(new SaveHrRosterCommand(null, null, Data()), default);
        row = await handlers.Handle(new TransitionHrRosterCommand(row.Id, row.Version, "Published"), default);
        var cancelled = await handlers.Handle(new TransitionHrRosterCommand(row.Id, row.Version, "Cancelled"), default);
        Assert.NotNull(cancelled.PublishedShift); Assert.Null(await store.PublishedAsync(1, 1, Day, default));
        var replacement = await handlers.Handle(new SaveHrRosterCommand(null, null, Data()), default);
        Assert.NotEqual(row.Id, replacement.Id); Assert.Equal(2, await db.Set<HrRosterAssignment>().CountAsync());
    }
    [Fact]
    public async Task AdjacentDifferentShiftPunchWindowsCannotOverlap()
    {
        var night = await calendars.Handle(new SaveHrShiftCommand(null, null, HrCalendarPersistenceTests.Shift()
            with { Code = "NIGHT", StartTimeLocal = new(10, 0), EndTimeLocal = new(2, 0), CrossesMidnight = true,
                MinimumWorkMinutes = 900, MinimumRestBeforeNextShiftMinutes = 0 }), default);
        var early = await calendars.Handle(new SaveHrShiftCommand(null, null, HrCalendarPersistenceTests.Shift()
            with { Code = "EARLY", StartTimeLocal = new(1, 0), EndTimeLocal = new(9, 0), MinimumRestBeforeNextShiftMinutes = 0 }), default);
        var first = await handlers.Handle(new SaveHrRosterCommand(null, null, Data() with { ShiftId = night.Id }), default);
        await handlers.Handle(new TransitionHrRosterCommand(first.Id, first.Version, "Published"), default);
        var next = await handlers.Handle(new SaveHrRosterCommand(null, null, Data() with { ShiftId = early.Id, WorkDate = Day.AddDays(1) }), default);
        await Assert.ThrowsAsync<HrCalendarConflictException>(() => handlers.Handle(new TransitionHrRosterCommand(next.Id, next.Version, "Published"), default));
    }
    [Fact]
    public async Task BulkFailureIsAtomicAndDuplicateInputRejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => handlers.Handle(new BulkAssignHrRosterCommand([Data(), Data()]), default));
        var count = await db.AuditEvents.CountAsync();
        var existing = await handlers.Handle(new SaveHrRosterCommand(null, null, Data() with { WorkDate = Day.AddDays(1) }), default);
        await Assert.ThrowsAsync<HrCalendarConflictException>(() => handlers.Handle(new BulkAssignHrRosterCommand([Data(), existing.Data]), default));
        Assert.Single(await db.Set<HrRosterAssignment>().ToListAsync()); Assert.Equal(count + 1, await db.AuditEvents.CountAsync());
    }

    [Fact]
    public async Task CrossMidnightAttendanceUsesOneShiftStartWorkDateAndProcessingIsIdempotent()
    {
        var nightShift = await calendars.Handle(new SaveHrShiftCommand(null, null,
            HrCalendarPersistenceTests.Shift() with
            {
                Code = "NIGHT",
                StartTimeLocal = new(10, 0),
                EndTimeLocal = new(2, 0),
                CrossesMidnight = true,
                MinimumWorkMinutes = 900,
                MaximumAllowedWorkMinutes = 960,
                Breaks = [new(1, 30, false)]
            }), default);
        var roster = await handlers.Handle(new SaveHrRosterCommand(null, null, Data() with { ShiftId = nightShift.Id }), default);
        roster = await handlers.Handle(new TransitionHrRosterCommand(roster.Id, roster.Version, "Published"), default);

        foreach (var (date, hour, direction) in new[]
        {
            (Day, 10, "IN"),
            (Day.AddDays(1), 2, "OUT")
        })
        {
            var input = new ManualAttendancePunchInput(1, date.ToDateTime(new(hour, 0)), "UTC", "Manual", direction);
            await audit.ExecuteAsync(new RequiredAuditOperation(1, "hr.attendance.manual_punch_recorded", "HR",
                "Recorded a manual punch.", "HrRawAttendancePunch", "manual"),
                t => rawPunches.AddManualAsync(1, input, new(1, null), t), default);
        }

        var processed = await attendanceHandlers.Handle(new ProcessHrAttendanceCommand(Day, Day), default);
        var rawVersions = await db.Set<HrRawAttendancePunch>().OrderBy(p => p.Id)
            .Select(p => p.ProcessingVersion).ToArrayAsync();
        var repeated = await attendanceHandlers.Handle(new ProcessHrAttendanceCommand(Day, Day), default);

        var daily = Assert.Single(processed);
        Assert.Equal(Day, daily.WorkDate);
        Assert.Equal(roster.Id, daily.RosterAssignmentId);
        Assert.Equal(new DateTime(Day.Year, Day.Month, Day.Day, 10, 0, 0), daily.FirstInAtLocal);
        Assert.Equal(Day.AddDays(1).ToDateTime(new(2, 0)), daily.LastOutAtLocal);
        Assert.Equal(930, daily.WorkedMinutes);
        Assert.Equal(30, daily.BreakMinutes);
        Assert.Equal("Present", daily.Status);
        Assert.Equal(1, daily.ProcessingVersion);
        Assert.Equal(rawVersions, await db.Set<HrRawAttendancePunch>().OrderBy(p => p.Id)
            .Select(p => p.ProcessingVersion).ToArrayAsync());
        var repeatedDaily = Assert.Single(repeated);
        Assert.Equal(daily.Id, repeatedDaily.Id);
        Assert.Equal(daily.ProcessingVersion, repeatedDaily.ProcessingVersion);
        Assert.Equal(daily.PunchIds, repeatedDaily.PunchIds);
        Assert.Equal(1, await db.Set<HrDailyAttendance>().CountAsync());
        Assert.Equal("Processed", (await db.Set<HrRawAttendancePunch>().OrderBy(p => p.Id).FirstAsync()).ProcessingStatus);

        await audit.ExecuteAsync(new RequiredAuditOperation(1, "hr.attendance.manual_punch_recorded", "HR",
            "Recorded a delayed manual punch.", "HrRawAttendancePunch", "manual"),
            t => rawPunches.AddManualAsync(1,
                new ManualAttendancePunchInput(1, Day.ToDateTime(new(12, 30)), "UTC", "Manual", "IN"),
                new(1, null), t), default);
        var delayed = Assert.Single(await attendanceHandlers.Handle(new ProcessHrAttendanceCommand(Day, Day), default));
        Assert.Equal(2, delayed.ProcessingVersion);
        Assert.Equal(daily.WorkedMinutes, delayed.WorkedMinutes);
        Assert.Equal(3, delayed.PunchIds.Count);
        Assert.Equal(3, await db.Set<HrRawAttendancePunch>().CountAsync(p => p.ProcessingStatus == "Processed"));
    }

    [Fact]
    public async Task AttendanceRetainsOverlappingHolidayAndWeeklyOffEvidence()
    {
        var calendar = await calendars.Handle(new SaveHrHolidayCalendarCommand(null, null,
            new("Company Calendar", new("Company"), Day, null)), default);
        await calendars.Handle(new SaveHrHolidayCommand(calendar.Id, null, null,
            new(Day, "H", "Holiday", "Public", true, null)), default);
        await calendars.Handle(new SaveHrWeeklyOffCommand(null, null,
            new(new("Company"), Day, null, [Day.DayOfWeek])), default);
        var roster = await handlers.Handle(new SaveHrRosterCommand(null, null, Data() with
        {
            DayClassification = "WeeklyOff"
        }), default);
        await handlers.Handle(new TransitionHrRosterCommand(roster.Id, roster.Version, "Published"), default);

        var daily = Assert.Single(await attendanceHandlers.Handle(new ProcessHrAttendanceCommand(Day, Day), default));
        var evidence = await db.Set<HrDailyAttendance>().SingleAsync();

        Assert.Equal("WeeklyOff", daily.Status);
        Assert.Equal("WeeklyOff", daily.DayClassification);
        Assert.Contains("\"IsHoliday\":true", evidence.CalendarEvidenceJson);
        Assert.Contains("\"IsWeeklyOff\":true", evidence.CalendarEvidenceJson);
    }

    [Fact]
    public async Task AttendanceProcessingCannotReadOrCreateRowsOutsideIdentityLocationScope()
    {
        var roster = await handlers.Handle(new SaveHrRosterCommand(null, null, Data()), default);
        await handlers.Handle(new TransitionHrRosterCommand(roster.Id, roster.Version, "Published"), default);
        var restrictedScope = new HrOperationalLocationReadScope(1, [new("Outlet", "999")]);

        var result = await audit.ExecuteAsync(new RequiredAuditOperation(1, "hr.attendance.processed", "HR",
            "Processed scoped attendance.", "HrDailyAttendance", $"{Day:yyyy-MM-dd}"),
            t => attendanceStore.ProcessAsync(1, Day, Day, null, restrictedScope, t), default);

        Assert.Empty(result);
        Assert.Empty(await db.Set<HrDailyAttendance>().ToListAsync());
        Assert.Empty(await attendanceStore.ListAsync(1, Day, Day, null, restrictedScope, default));
    }

    [Fact]
    public async Task AttendanceAuditFailureRollsBackDailyStateAndRawProcessingMetadata()
    {
        var roster = await handlers.Handle(new SaveHrRosterCommand(null, null, Data()), default);
        await handlers.Handle(new TransitionHrRosterCommand(roster.Id, roster.Version, "Published"), default);
        var raw = await audit.ExecuteAsync(new RequiredAuditOperation(1, "hr.attendance.manual_punch_recorded", "HR",
            "Recorded a manual punch.", "HrRawAttendancePunch", "manual"),
            t => rawPunches.AddManualAsync(1,
                new ManualAttendancePunchInput(1, Day.ToDateTime(new(9, 0)), "UTC", "Manual", "IN"),
                new(1, null), t), default);
        var count = await db.AuditEvents.CountAsync();
        failure.Fail = true;

        await Assert.ThrowsAsync<IOException>(() =>
            attendanceHandlers.Handle(new ProcessHrAttendanceCommand(Day, Day), default));

        db.ChangeTracker.Clear();
        Assert.Empty(await db.Set<HrDailyAttendance>().ToListAsync());
        var persistedPunch = await db.Set<HrRawAttendancePunch>().SingleAsync(p => p.Id == raw.Id);
        Assert.Equal("Pending", persistedPunch.ProcessingStatus);
        Assert.Equal(0, persistedPunch.ProcessingVersion);
        Assert.Equal(count, await db.AuditEvents.CountAsync());
    }
    public async Task DisposeAsync() { await db.DisposeAsync(); await connection.DisposeAsync(); }
    private sealed class Clock : TimeProvider {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 9, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class FailAudit : SaveChangesInterceptor {
        public bool Fail { get; set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default) {
            if (Fail && data.Context!.ChangeTracker.Entries<AuditEvent>().Any(e => e.State == EntityState.Added))
                throw new IOException("Injected roster audit failure.");
            return base.SavingChangesAsync(data, result, ct);
        }
    }
}
