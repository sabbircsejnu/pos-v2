using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RetailPOS.Application.HR.Calendars;
using RetailPOS.Application.HR.Organisation;
using RetailPOS.Core.Audit;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Core.Entities.HR.Calendars;
using RetailPOS.Core.Entities.HR.Organisation;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.AuditEvents;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.HR.Calendars;

namespace RetailPOS.IntegrationTests.HR;

public sealed class HrCalendarPersistenceTests : IAsyncLifetime
{
    private readonly SqliteConnection connection = new("Data Source=:memory:;Foreign Keys=True");
    private RetailPOSDbContext db = null!;
    private HrCalendarHandlers handlers = null!;
    private HrCalendarStore store = null!;
    private readonly FailCalendarAudit failure = new();
    private readonly Clock clock = new();
    private DateOnly Start => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime).AddDays(1);

    public async Task InitializeAsync()
    {
        await connection.OpenAsync();
        var context = new AuditContext();
        db = new(new DbContextOptionsBuilder<RetailPOSDbContext>().UseSqlite(connection)
            .AddInterceptors(new AuditChangeTrackerInterceptor(context, NullLogger<AuditChangeTrackerInterceptor>.Instance), failure).Options);
        await db.Database.EnsureCreatedAsync();
        db.Businesses.AddRange(new Business { Id = 1, Name = "Calendar" }, new Business { Id = 2, Name = "Other" });
        db.Outlets.Add(new Outlet { Id = 10, BusinessId = 1, Name = "Outlet" });
        db.OperationalLocationProjections.AddRange(
            Projection(10, 1, "10"), Projection(20, 2, "20"), Projection(30, 1, "999"),
            Projection(40, 1, "40", false));
        await db.SaveChangesAsync();
        context.Initialize(new AuditContextSnapshot(Guid.NewGuid(), 101, 101, 5, 7, null, 1, AuditSource.API,
            "POST", "/api/hr/v1/shifts", null, null, null, false, null, null, null, null, "actor", "HR"));
        store = new(db, clock);
        handlers = new(new Access(), store, new RequiredAuditTransaction(db, context,
            new AuditService(db, context, Mock.Of<IServiceProvider>(), NullLogger<AuditService>.Instance),
            NullLogger<RequiredAuditTransaction>.Instance), clock);
    }

    [Fact]
    public async Task ShiftsAreBusinessScopedAuditedAndStaleWritesRejected()
    {
        var s = await handlers.Handle(new SaveHrShiftCommand(null, null, Shift()), default);
        Assert.Equal(1, await db.AuditEvents.CountAsync());
        var updated = await handlers.Handle(new SaveHrShiftCommand(s.Id, s.Version, Shift() with { Name = "New", Breaks = [new(1, 30, false)] }), default);
        Assert.Equal(2, updated.Version); Assert.Single(updated.Data.Breaks);
        await Assert.ThrowsAsync<HrCalendarConflictException>(() => handlers.Handle(new SaveHrShiftCommand(s.Id, s.Version, Shift()), default));
        await Assert.ThrowsAsync<HrCalendarConflictException>(() => handlers.Handle(new SaveHrShiftCommand(null, null, Shift() with { Code = "day" }), default));
        Assert.Empty(await store.ShiftsAsync(2, null, default));
        await using var tx = await db.Database.BeginTransactionAsync();
        Assert.Equal(2, (await store.SaveShiftAsync(2, null, null, Shift(), default)).BusinessId);
        await tx.RollbackAsync(); db.ChangeTracker.Clear();
    }

    [Theory]
    [InlineData("Company")] [InlineData("LocationType")] [InlineData("Location")]
    public async Task HolidaysResolveAtEachSupportedScope(string type)
    {
        var scope = Scope(type);
        var calendar = await handlers.Handle(new SaveHrHolidayCalendarCommand(null, null, new("Calendar", scope, Start, null)), default);
        var holiday = await handlers.Handle(new SaveHrHolidayCommand(calendar.Id, null, null, new(Start, "H", "Public holiday", "Public", true, "Note")), default);
        var result = await handlers.Handle(new GetHrDayClassificationQuery(Start, 10), default);
        Assert.Equal("Holiday", result.Classification); Assert.Equal(holiday.Id, Assert.Single(result.Holidays).Id);
        var changed = await handlers.Handle(new SaveHrHolidayCommand(calendar.Id, holiday.Id, holiday.Version, holiday.Data with { Name = "Renamed" }), default);
        await Assert.ThrowsAsync<HrCalendarConflictException>(() => handlers.Handle(new SaveHrHolidayCommand(calendar.Id, holiday.Id, holiday.Version, holiday.Data), default));
        Assert.Equal(2, changed.Version);
        await handlers.Handle(new SaveHrHolidayCommand(calendar.Id, holiday.Id, changed.Version, null), default);
        Assert.Equal("NormalWorkingDay", (await store.ClassifyAsync(1, Start, 10, default)).Classification);
    }

    [Theory]
    [InlineData(20)] [InlineData(30)] [InlineData(40)] [InlineData(999)]
    public async Task CrossBusinessUnownedInactiveAndUnknownLocationsRejected(long id)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => handlers.Handle(new SaveHrHolidayCalendarCommand(null, null,
            new("Invalid", new("Location", null, id), Start, null)), default));
        Assert.Empty(await db.Set<HrHolidayCalendar>().ToListAsync()); Assert.Empty(await db.AuditEvents.ToListAsync());
    }

    [Fact]
    public async Task PolicyPrecedenceSelectsMostSpecificEvenWhenThatDayIsWorking()
    {
        await Policy(new("Company"), [Start.DayOfWeek]);
        await Policy(new("LocationType", "Outlet"), [Start.DayOfWeek]);
        var specific = await Policy(new("Location", null, 10), [Start.AddDays(1).DayOfWeek]);
        var result = await store.ClassifyAsync(1, Start, 10, default);
        Assert.Equal(specific.Id, result.WeeklyOffPolicy!.Id); Assert.False(result.IsWeeklyOff);
        Assert.Equal("NormalWorkingDay", result.Classification);
    }

    [Theory]
    [InlineData("Company")] [InlineData("LocationType")] [InlineData("Location")]
    public async Task EffectiveVersionsRetainHistoryAndRejectOverlapAndStaleSuccessor(string scopeType)
    {
        var scope = Scope(scopeType);
        var first = await Policy(scope, [Start.DayOfWeek]);
        await Assert.ThrowsAsync<HrCalendarConflictException>(() => Policy(scope, [DayOfWeek.Sunday]));
        var successorStart = Start.AddDays(7);
        var second = await handlers.Handle(new SaveHrWeeklyOffCommand(first.Id, first.Version,
            new(scope, successorStart, null, [successorStart.AddDays(1).DayOfWeek])), default);
        Assert.Equal(2, second.VersionNo);
        Assert.Equal(first.Id, (await store.ClassifyAsync(1, Start, 10, default)).WeeklyOffPolicy!.Id);
        Assert.Equal(second.Id, (await store.ClassifyAsync(1, successorStart, 10, default)).WeeklyOffPolicy!.Id);
        Assert.True((await store.ClassifyAsync(1, Start, 10, default)).IsWeeklyOff);
        await Assert.ThrowsAsync<HrCalendarConflictException>(() => handlers.Handle(new SaveHrWeeklyOffCommand(first.Id, 1,
            new(scope, successorStart.AddDays(1), null, [DayOfWeek.Friday])), default));
        var rows = await handlers.Handle(new GetHrWeeklyOffQuery(), default);
        Assert.Equal(Start.AddDays(6), rows.Single(r => r.Id == first.Id).Data.EffectiveTo);
        Assert.Equal(2, rows.Count);
        var historicalDate = Start;
        clock.Now = clock.Now.AddDays(14);
        Assert.True((await store.ClassifyAsync(1, historicalDate, 10, default)).IsWeeklyOff);
    }

    [Fact]
    public async Task HolidayWeeklyOverlapPreservesBothEvidenceWithoutBenefitsCalculation()
    {
        await Policy(new("Company"), [Start.DayOfWeek]);
        var c = await handlers.Handle(new SaveHrHolidayCalendarCommand(null, null, new("Public", new("Company"), Start, null)), default);
        await handlers.Handle(new SaveHrHolidayCommand(c.Id, null, null, new(Start, "H", "Holiday", "Public", true, null)), default);
        var r = await store.ClassifyAsync(1, Start, 10, default);
        Assert.Equal("Holiday", r.Classification); Assert.True(r.IsHoliday); Assert.True(r.IsWeeklyOff);
        Assert.NotNull(r.WeeklyOffPolicy); Assert.Single(r.Holidays);
    }

    [Theory]
    [InlineData("shift")] [InlineData("calendar")] [InlineData("holiday")] [InlineData("weekly")]
    public async Task AuditFailureRollsBackEveryCriticalConfiguration(string resource)
    {
        var calendar = await handlers.Handle(new SaveHrHolidayCalendarCommand(null, null, new("Public", new("Company"), Start, null)), default);
        var before = await db.AuditEvents.CountAsync();
        failure.Fail = true;
        await Assert.ThrowsAsync<IOException>(async () =>
        {
            switch (resource)
            {
                case "shift": await handlers.Handle(new SaveHrShiftCommand(null, null, Shift()), default); break;
                case "calendar": await handlers.Handle(new SaveHrHolidayCalendarCommand(null, null, new("Other", new("Company"), Start, null)), default); break;
                case "holiday": await handlers.Handle(new SaveHrHolidayCommand(calendar.Id, null, null, new(Start, "H", "Holiday", "Public", true, null)), default); break;
                case "weekly": await Policy(new("Company"), [DayOfWeek.Sunday]); break;
            }
        });
        Assert.Equal(before, await db.AuditEvents.CountAsync());
        Assert.Empty(await db.Set<HrShift>().ToListAsync()); Assert.Single(await db.Set<HrHolidayCalendar>().ToListAsync());
        Assert.Empty(await db.Set<HrHoliday>().ToListAsync()); Assert.Empty(await db.Set<HrWeeklyOffPolicyVersion>().ToListAsync());
    }

    [Fact]
    public async Task CalendarStaleUpdateAndHistoricalMutationRejectedAndFactoryIsOnlyAType()
    {
        var c = await handlers.Handle(new SaveHrHolidayCalendarCommand(null, null, new("Factory future", new("LocationType", "Factory"), Start, null)), default);
        var updated = await handlers.Handle(new SaveHrHolidayCalendarCommand(c.Id, c.Version, c.Data with { Name = "New" }), default);
        await Assert.ThrowsAsync<HrCalendarConflictException>(() => handlers.Handle(new SaveHrHolidayCalendarCommand(c.Id, c.Version, c.Data), default));
        clock.Now = clock.Now.AddDays(2);
        await Assert.ThrowsAsync<HrCalendarConflictException>(() => handlers.Handle(new SaveHrHolidayCalendarCommand(c.Id, updated.Version,
            c.Data with { EffectiveFrom = Start.AddDays(10) }), default));
        Assert.False(await db.OperationalLocationProjections.AnyAsync(p => p.SourceEntityType == "Factory"));
    }

    [Fact]
    public async Task VersionDaysCannotBeOverwrittenAndScheduledClosureRetainsHistoricalMeaning()
    {
        var p = await Policy(new("Company"), [Start.DayOfWeek]);
        await handlers.Handle(new SaveHrWeeklyOffCommand(p.Id, p.Version, null, Start.AddDays(3)), default);
        Assert.True((await store.ClassifyAsync(1, Start, 10, default)).IsWeeklyOff);
        Assert.Null((await store.ClassifyAsync(1, Start.AddDays(4), 10, default)).WeeklyOffPolicy);
        var day = await db.Set<HrWeeklyOffPolicyDay>().SingleAsync();
        day.DayOfWeek = Start.AddDays(1).DayOfWeek;
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    private Task<HrWeeklyOffResponse> Policy(HrCalendarScope scope, IReadOnlyList<DayOfWeek> days) =>
        handlers.Handle(new SaveHrWeeklyOffCommand(null, null, new(scope, Start, null, days)), default);
    private static HrCalendarScope Scope(string type) => type switch
    {
        "Location" => new(type, null, 10), "LocationType" => new(type, "Outlet"), _ => new(type)
    };
    internal static HrShiftData Shift() => new("DAY", "Day", new(9, 0), new(17, 0), false, 420, null, 15, 15, 660, 5, 5, true, []);
    private static OperationalLocationProjection Projection(long id, long business, string source, bool active = true) =>
        new() { Id = id, BusinessId = business, SourceSystem = "RetailPOS", SourceEntityType = "Outlet",
            SourceEntityId = source, Name = "Location", IsSourceActive = active, SourceVersion = 1 };
    public async Task DisposeAsync() { await db.DisposeAsync(); await connection.DisposeAsync(); }
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    internal sealed class Access : IHrOrganisationAccess
    {
        public long RequireBusinessId() => 1;
        public long RequireRealUserId() => 101;
        public Task EnsureAsync(long b, HrOrganisationOperation o, long? l, CancellationToken ct) => Task.CompletedTask;
        public Task EnsureLocationAccessibleAsync(long b, long l, CancellationToken ct) => Task.CompletedTask;
        public Task<HrOperationalLocationReadScope> GetLocationReadScopeAsync(long b, CancellationToken ct) => Task.FromResult(new HrOperationalLocationReadScope(b, null));
    }
    private sealed class FailCalendarAudit : SaveChangesInterceptor
    {
        public bool Fail { get; set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (Fail && data.Context!.ChangeTracker.Entries<AuditEvent>().Any(e => e.State == EntityState.Added))
                throw new IOException("Injected calendar audit failure.");
            return base.SavingChangesAsync(data, result, ct);
        }
    }
}
