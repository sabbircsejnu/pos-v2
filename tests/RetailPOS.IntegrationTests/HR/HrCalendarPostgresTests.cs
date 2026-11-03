using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RetailPOS.Application.HR.Calendars;
using RetailPOS.Core.Audit;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Core.Entities.HR.Calendars;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.AuditEvents;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.HR.Calendars;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.IntegrationTests.HR;

public sealed class HrCalendarPostgresTests
{
    [Theory]
    [InlineData("shift-update")] [InlineData("holiday-update")] [InlineData("weekly-version")]
    [InlineData("weekly-create")] [InlineData("shift-code")]
    public async Task FullMigrationChainAndConcurrentConfigurationHaveOneAuditedOutcome(string scenario)
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        var configuration = new ConfigurationBuilder().AddJsonFile(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "RetailPOS.API", "appsettings.json"))).Build();
        await using var database = await PostgresTestDatabase.CreateAsync(
            Environment.GetEnvironmentVariable("RETAILPOS_TEST_POSTGRES")
                ?? configuration.GetConnectionString("DefaultConnection")!, PostgresTestDatabasePrefix.MigrationBaseline);
        var options = new DbContextOptionsBuilder<RetailPOSDbContext>().UseNpgsql(database.ConnectionString).Options;
        await using (var setup = new RetailPOSDbContext(options))
        {
            Assert.False(setup.Database.HasPendingModelChanges());
            await setup.Database.MigrateAsync();
            Assert.Equal(setup.Database.GetMigrations(), await setup.Database.GetAppliedMigrationsAsync());
            await setup.Database.MigrateAsync();
            Assert.Empty(await setup.Database.GetPendingMigrationsAsync());
            setup.Businesses.Add(new Business { Id = 1, Name = "Calendar provider" });
            await setup.SaveChangesAsync();
        }
        var start = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);
        HrShiftResponse? shift = null; HrHolidayResponse? holiday = null; HrWeeklyOffResponse? policy = null;
        long calendarId = 0;
        await using (var seed = new Session(database.ConnectionString))
        {
            if (scenario == "shift-update") shift = await seed.Handlers.Handle(new SaveHrShiftCommand(null, null, HrCalendarPersistenceTests.Shift()), default);
            if (scenario == "holiday-update")
            {
                calendarId = (await seed.Handlers.Handle(new SaveHrHolidayCalendarCommand(null, null, new("Holiday", new("Company"), start, null)), default)).Id;
                holiday = await seed.Handlers.Handle(new SaveHrHolidayCommand(calendarId, null, null, new(start, "PUBLIC", "Public", "Public", true, null)), default);
            }
            if (scenario == "weekly-version")
                policy = await seed.Handlers.Handle(new SaveHrWeeklyOffCommand(null, null, new(new("Company"), start, null, [start.DayOfWeek])), default);
        }

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<bool> Race()
        {
            await using var s = new Session(database.ConnectionString);
            await gate.Task;
            try
            {
                switch (scenario)
                {
                    case "shift-update":
                        await s.Handlers.Handle(new SaveHrShiftCommand(shift!.Id, shift.Version, shift.Data with { Name = "Updated" }), default); break;
                    case "holiday-update":
                        await s.Handlers.Handle(new SaveHrHolidayCommand(calendarId, holiday!.Id, holiday.Version, holiday.Data with { Name = "Updated" }), default); break;
                    case "weekly-version":
                        await s.Handlers.Handle(new SaveHrWeeklyOffCommand(policy!.Id, policy.Version, new(new("Company"), start.AddDays(7), null, [DayOfWeek.Monday])), default); break;
                    case "weekly-create":
                        await s.Handlers.Handle(new SaveHrWeeklyOffCommand(null, null, new(new("Company"), start, null, [DayOfWeek.Friday])), default); break;
                    case "shift-code":
                        await s.Handlers.Handle(new SaveHrShiftCommand(null, null, HrCalendarPersistenceTests.Shift()), default); break;
                }
                return true;
            }
            catch (HrCalendarConflictException) { return false; }
        }
        var a = Race(); var b = Race(); gate.SetResult();
        var outcomes = await Task.WhenAll(a, b); Assert.Single(outcomes, x => x);
        await using var verify = new RetailPOSDbContext(options);
        Assert.Equal(scenario == "holiday-update" ? 3 : scenario is "weekly-create" or "shift-code" ? 1 : 2,
            await verify.AuditEvents.CountAsync());
        if (scenario == "weekly-version")
        {
            var versions = await verify.Set<HrWeeklyOffPolicyVersion>().OrderBy(r => r.VersionNo).ToListAsync();
            Assert.Equal(2, versions.Count); Assert.Equal(start.AddDays(6), versions[0].EffectiveTo);
            Assert.Equal(2, versions[1].VersionNo);
        }
        if (scenario == "weekly-create") Assert.Equal(1, await verify.Set<HrWeeklyOffPolicyVersion>().CountAsync());
        if (scenario.StartsWith("shift")) Assert.Equal(1, await verify.Set<HrShift>().CountAsync());
    }

    private sealed class Session : IAsyncDisposable
    {
        private readonly RetailPOSDbContext db;
        public HrCalendarHandlers Handlers { get; }
        public Session(string connection)
        {
            var context = new AuditContext();
            context.Initialize(new AuditContextSnapshot(Guid.NewGuid(), 101, 101, 5, 7, null, 1, AuditSource.API,
                "POST", "/api/hr/v1/shifts", null, null, null, false, null, null, null, null, "actor", "HR"));
            db = new(new DbContextOptionsBuilder<RetailPOSDbContext>().UseNpgsql(connection)
                .AddInterceptors(new AuditChangeTrackerInterceptor(context, NullLogger<AuditChangeTrackerInterceptor>.Instance)).Options);
            Handlers = new(new HrCalendarPersistenceTests.Access(), new HrCalendarStore(db, TimeProvider.System),
                new RequiredAuditTransaction(db, context, new AuditService(db, context, Mock.Of<IServiceProvider>(),
                    NullLogger<AuditService>.Instance), NullLogger<RequiredAuditTransaction>.Instance), TimeProvider.System);
        }
        public ValueTask DisposeAsync() => db.DisposeAsync();
    }
}
