using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RetailPOS.Application.HR.Attendance;
using RetailPOS.Application.HR.Calendars;
using RetailPOS.Core.Audit;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Core.Entities.HR.Attendance;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.AuditEvents;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.HR.Attendance;
using RetailPOS.Infrastructure.HR.Calendars;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.IntegrationTests.HR;

public sealed class HrRosterPostgresTests
{
    [Fact]
    public async Task ConcurrentAttendanceProcessingCreatesOneIdempotentDailyRow()
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
        var day = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);
        await using (var session = new Session(database.ConnectionString))
        {
            var shift = await session.Calendars.Handle(new SaveHrShiftCommand(null, null,
                HrCalendarPersistenceTests.Shift() with
                {
                    Code = "NIGHT",
                    StartTimeLocal = new(10, 0),
                    EndTimeLocal = new(2, 0),
                    CrossesMidnight = true,
                    MinimumWorkMinutes = 900,
                    MaximumAllowedWorkMinutes = 960,
                    AllowedEarlyPunchMinutes = 0,
                    AllowedLatePunchMinutes = 0,
                    Breaks = [new(1, 30, false)]
                }), default);
            var roster = await session.Rosters.Handle(new SaveHrRosterCommand(null, null, new(1, day, shift.Id, 10)), default);
            await session.Rosters.Handle(new TransitionHrRosterCommand(roster.Id, roster.Version, "Published"), default);
            foreach (var (date, hour, direction) in new[] { (day, 10, "IN"), (day.AddDays(1), 2, "OUT") })
                await session.Punches.Handle(new AddManualAttendancePunchCommand(new ManualAttendancePunchInput(1,
                    date.ToDateTime(new(hour, 0)), "UTC", "Manual", direction)), default);
        }

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<IReadOnlyList<HrDailyAttendanceResponse>> Process()
        {
            await using var session = new Session(database.ConnectionString);
            await gate.Task;
            return await session.Attendance.Handle(new ProcessHrAttendanceCommand(day, day), default);
        }
        var first = Process();
        var second = Process();
        gate.SetResult();
        var results = await Task.WhenAll(first, second);

        await using var verify = new RetailPOSDbContext(new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseNpgsql(database.ConnectionString).Options);
        var daily = await verify.Set<HrDailyAttendance>().SingleAsync();
        Assert.All(results, rows => Assert.Single(rows));
        Assert.Equal(930, daily.WorkedMinutes);
        Assert.Equal(1, daily.ProcessingVersion);
        Assert.Equal(2, await verify.Set<HrRawAttendancePunch>().CountAsync(p => p.ProcessingStatus == "Processed"));
        Assert.Equal(7, await verify.AuditEvents.CountAsync());
    }

    [Theory]
    [InlineData("duplicate")] [InlineData("update")] [InlineData("publish")]
    [InlineData("device-code")] [InlineData("device-serial")] [InlineData("device-mapping")] [InlineData("device-close")]
    public async Task FullChainAndConcurrentRosterHaveOneAuditedOutcome(string scenario)
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        var configuration = new ConfigurationBuilder().AddJsonFile(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "RetailPOS.API", "appsettings.json"))).Build();
        await using var database = await PostgresTestDatabase.CreateAsync(Environment.GetEnvironmentVariable("RETAILPOS_TEST_POSTGRES")
            ?? configuration.GetConnectionString("DefaultConnection")!, PostgresTestDatabasePrefix.MigrationBaseline);
        await using (var setup = new RetailPOSDbContext(new DbContextOptionsBuilder<RetailPOSDbContext>().UseNpgsql(database.ConnectionString).Options))
        {
            Assert.False(setup.Database.HasPendingModelChanges()); await setup.Database.MigrateAsync();
            Assert.Equal(setup.Database.GetMigrations(), await setup.Database.GetAppliedMigrationsAsync());
            await setup.Database.MigrateAsync(); await HrRosterPersistenceTests.Seed(setup);
            setup.Outlets.Add(new RetailPOS.Core.Entities.Outlet { Id = 20, BusinessId = 2, Name = "Other" });
            await setup.SaveChangesAsync();
        }
        var day = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);
        HrRosterResponse? existing = null; long shift = 0;
        HrDeviceResponse? device = null; HrDeviceMappingResponse? mapping = null;
        await using (var s = new Session(database.ConnectionString))
        {
            if (!scenario.StartsWith("device"))
            {
                shift = (await s.Calendars.Handle(new SaveHrShiftCommand(null, null, HrCalendarPersistenceTests.Shift()), default)).Id;
                if (scenario != "duplicate") existing = await s.Rosters.Handle(new SaveHrRosterCommand(null, null, new(1, day, shift, 10)), default);
            }
            if (scenario is "device-mapping" or "device-close")
                device = await s.Devices.Handle(new SaveHrDeviceCommand(null, null, HrDevicePersistenceTests.Data()), default);
            if (scenario == "device-close")
                mapping = await s.Devices.Handle(new CreateHrDeviceMappingCommand(device!.Id, new(1, "23", HrDevicePersistenceTests.Start)), default);
        }
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<bool> Race(bool second)
        {
            await using var s = new Session(database.ConnectionString, scenario == "device-serial" && second ? 2 : 1); await gate.Task;
            try {
                if (scenario == "device-code") await s.Devices.Handle(new SaveHrDeviceCommand(null, null,
                    HrDevicePersistenceTests.Data() with { SerialNumber = second ? "B" : "A" }), default);
                else if (scenario == "device-serial") await s.Devices.Handle(new SaveHrDeviceCommand(null, null,
                    HrDevicePersistenceTests.Data() with { DeviceCode = second ? "B" : "A", OperationalLocationProjectionId = second ? 20 : 10 }), default);
                else if (scenario == "device-mapping") await s.Devices.Handle(new CreateHrDeviceMappingCommand(device!.Id,
                    new(1, "23", HrDevicePersistenceTests.Start)), default);
                else if (scenario == "device-close") await s.Devices.Handle(new CloseHrDeviceMappingCommand(mapping!.Id,
                    mapping.Version, HrDevicePersistenceTests.Start.AddDays(1)), default);
                else if (scenario == "publish") await s.Rosters.Handle(new TransitionHrRosterCommand(existing!.Id, existing.Version, "Published"), default);
                else await s.Rosters.Handle(new SaveHrRosterCommand(existing?.Id, existing?.Version, new(1, day, shift, 10)), default);
                return true;
            } catch (HrCalendarConflictException) { return false; }
        }
        var a = Race(false); var b = Race(true); gate.SetResult(); Assert.Single(await Task.WhenAll(a, b), x => x);
        await using var verify = new RetailPOSDbContext(new DbContextOptionsBuilder<RetailPOSDbContext>().UseNpgsql(database.ConnectionString).Options);
        if (scenario.StartsWith("device"))
        {
            Assert.Single(await verify.Set<HrAttendanceDevice>().ToListAsync());
            Assert.Equal(scenario == "device-close" ? 3 : scenario == "device-mapping" ? 2 : 1, await verify.AuditEvents.CountAsync());
            if (scenario == "device-close") Assert.False((await verify.Set<HrDeviceUserMapping>().SingleAsync()).IsActive);
            if (scenario == "device-mapping") Assert.True((await verify.Set<HrDeviceUserMapping>().SingleAsync()).IsActive);
            return;
        }
        var row = await verify.Set<HrRosterAssignment>().SingleAsync();
        Assert.Equal(scenario == "duplicate" ? 1 : 2, row.Version);
        Assert.Equal(scenario == "publish" ? "Published" : "Draft", row.Status);
        Assert.Equal(scenario == "duplicate" ? 2 : 3, await verify.AuditEvents.CountAsync());
    }
    private sealed class Session : IAsyncDisposable
    {
        private readonly RetailPOSDbContext db;
        public HrCalendarHandlers Calendars { get; }
        public HrRosterHandlers Rosters { get; }
        public HrDeviceHandlers Devices { get; }
        public HrRawAttendancePunchHandlers Punches { get; }
        public HrAttendanceProcessingHandlers Attendance { get; }
        public Session(string connection, long business = 1)
        {
            var context = new AuditContext();
            context.Initialize(new AuditContextSnapshot(Guid.NewGuid(), 101, 101, 5, 7, null, business, AuditSource.API,
                "POST", "/api/hr/v1/rosters", null, null, null, false, null, null, null, null, "actor", "HR"));
            db = new(new DbContextOptionsBuilder<RetailPOSDbContext>().UseNpgsql(connection)
                .AddInterceptors(new AuditChangeTrackerInterceptor(context, NullLogger<AuditChangeTrackerInterceptor>.Instance)).Options);
            var audit = new RequiredAuditTransaction(db, context, new AuditService(db, context, Mock.Of<IServiceProvider>(),
                NullLogger<AuditService>.Instance), NullLogger<RequiredAuditTransaction>.Instance);
            var calendars = new HrCalendarStore(db, TimeProvider.System);
            Calendars = new(new HrCalendarPersistenceTests.Access(), calendars, audit, TimeProvider.System);
            Rosters = new(new HrCalendarPersistenceTests.Access(), new HrRosterStore(db, calendars, TimeProvider.System), audit);
            var access = new Mock<RetailPOS.Application.HR.Organisation.IHrOrganisationAccess>();
            access.Setup(a => a.RequireBusinessId()).Returns(business); access.Setup(a => a.RequireRealUserId()).Returns(101);
            access.Setup(a => a.EnsureAsync(It.IsAny<long>(), It.IsAny<RetailPOS.Application.HR.Organisation.HrOrganisationOperation>(),
                It.IsAny<long?>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            access.Setup(a => a.EnsureLocationAccessibleAsync(It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            access.Setup(a => a.GetLocationReadScopeAsync(business, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new RetailPOS.Application.HR.Organisation.HrOperationalLocationReadScope(business, null));
            Devices = new(access.Object, new HrDeviceStore(db, TimeProvider.System), audit);
            var punchStore = new HrRawAttendancePunchStore(db, TimeProvider.System);
            Punches = new(access.Object, punchStore, audit);
            Attendance = new(access.Object, new HrAttendanceProcessingStore(db, TimeProvider.System), audit);
        }
        public ValueTask DisposeAsync() => db.DisposeAsync();
    }
}
