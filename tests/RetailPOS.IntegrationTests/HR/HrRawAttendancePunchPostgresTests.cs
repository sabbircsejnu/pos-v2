using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.HR.Attendance;
using RetailPOS.Application.HR.Organisation;
using RetailPOS.Core.Audit;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Core.Entities.HR.Attendance;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.AuditEvents;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.HR.Attendance;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.IntegrationTests.HR;

public sealed class HrRawAttendancePunchPostgresTests
{
    [Fact]
    public async Task ConcurrentManualDuplicateCreatesOneRawEvidenceRow()
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
            Assert.Equal(setup.Database.GetMigrations(), await setup.Database.GetAppliedMigrationsAsync());
            await setup.Database.MigrateAsync();
            await HrRosterPersistenceTests.Seed(setup);
        }

        var day = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);
        var input = new ManualAttendancePunchInput(1, day.ToDateTime(new TimeOnly(9, 0)), "UTC", "Manual", "IN");
        await using var first = new Session(database.ConnectionString);
        await using var second = new Session(database.ConnectionString);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<RawAttendancePunchResponse> Ingest(Session session)
        {
            await gate.Task;
            return await session.Handler.Handle(new AddManualAttendancePunchCommand(input), default);
        }
        var a = Ingest(first);
        var b = Ingest(second);
        gate.SetResult();
        var results = await Task.WhenAll(a, b);
        Assert.Single(results.Select(r => r.Id).Distinct());
        Assert.Single(results, r => !r.Duplicate);
        Assert.Single(results, r => r.Duplicate);
        await using var verify = new RetailPOSDbContext(new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseNpgsql(database.ConnectionString).Options);
        Assert.Single(await verify.Set<HrRawAttendancePunch>().ToListAsync());
        Assert.Equal(2, await verify.AuditEvents.CountAsync());
    }

    private sealed class Session : IAsyncDisposable
    {
        private readonly RetailPOSDbContext db;
        public HrRawAttendancePunchHandlers Handler { get; }

        public Session(string connection)
        {
            var context = new AuditContext();
            context.Initialize(new AuditContextSnapshot(Guid.NewGuid(), 101, 101, 5, 7, null, 1,
                AuditSource.API, "POST", "/api/hr/v1/attendance/manual-punches", null, null, null,
                false, null, null, null, null, "actor", "HR"));
            db = new(new DbContextOptionsBuilder<RetailPOSDbContext>().UseNpgsql(connection)
                .AddInterceptors(new AuditChangeTrackerInterceptor(context,
                    NullLogger<AuditChangeTrackerInterceptor>.Instance)).Options);
            IRequiredAuditTransaction audit = new RequiredAuditTransaction(db, context,
                new AuditService(db, context, Mock.Of<IServiceProvider>(), NullLogger<AuditService>.Instance),
                NullLogger<RequiredAuditTransaction>.Instance);
            var access = new Mock<IHrOrganisationAccess>();
            access.Setup(a => a.RequireBusinessId()).Returns(1);
            access.Setup(a => a.RequireRealUserId()).Returns(101);
            access.Setup(a => a.EnsureAsync(1, HrOrganisationOperation.CreateManualAttendancePunch, null,
                It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            access.Setup(a => a.EnsureLocationAccessibleAsync(1, 10, It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            access.Setup(a => a.GetLocationReadScopeAsync(1, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new HrOperationalLocationReadScope(1, null));
            Handler = new(access.Object, new HrRawAttendancePunchStore(db, TimeProvider.System), audit);
        }

        public ValueTask DisposeAsync() => db.DisposeAsync();
    }
}
