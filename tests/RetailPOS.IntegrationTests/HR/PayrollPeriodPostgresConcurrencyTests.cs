using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.HR.Payroll;
using RetailPOS.Core.Audit;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.AuditEvents;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.HR.Payroll;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.IntegrationTests.HR;

public sealed class PayrollPeriodPostgresConcurrencyTests
{
    [Fact]
    public async Task ConcurrentOverlappingCreateAndProcessingTransitionsHaveOneDurableOutcome()
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        var settingsPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "RetailPOS.API", "appsettings.json"));
        var connectionString = Environment.GetEnvironmentVariable("RETAILPOS_TEST_POSTGRES")
            ?? new ConfigurationBuilder().AddJsonFile(settingsPath, optional: false)
                .Build().GetConnectionString("DefaultConnection");
        Assert.False(string.IsNullOrWhiteSpace(connectionString));

        await using var database = await PostgresTestDatabase.CreateAsync(
            connectionString!, PostgresTestDatabasePrefix.MigrationBaseline);
        await using (var setup = CreateDb(database.ConnectionString))
        {
            Assert.False(setup.Database.HasPendingModelChanges());
            await setup.Database.MigrateAsync();
            setup.Businesses.Add(new Business { Id = 1, Name = "Payroll" });
            await setup.SaveChangesAsync();
        }

        await using var firstCreate = new Session(database.ConnectionString);
        await using var secondCreate = new Session(database.ConnectionString);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<(PayrollPeriodResponse? Period, Exception? Error)> Create(
            Session session, CreatePayrollPeriodRequest request)
        {
            await gate.Task;
            try { return (await session.CreateAsync(request), null); }
            catch (Exception exception) { return (null, exception); }
        }

        var requestOne = new CreatePayrollPeriodRequest(
            "2026-10-A", new(2026, 10, 1), new(2026, 10, 31), null);
        var requestTwo = new CreatePayrollPeriodRequest(
            "2026-10-B", new(2026, 10, 15), new(2026, 11, 14), null);
        var createOne = Create(firstCreate, requestOne);
        var createTwo = Create(secondCreate, requestTwo);
        gate.SetResult();
        var createResults = await Task.WhenAll(createOne, createTwo);
        Assert.Single(createResults, result => result.Period is not null);
        Assert.Single(createResults, result => result.Error is PayrollPeriodConflictException);
        var createdId = createResults.Single(result => result.Period is not null).Period!.Id;

        await using var firstTransition = new Session(database.ConnectionString);
        await using var secondTransition = new Session(database.ConnectionString);
        var transitionGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<PayrollPeriodResponse> Transition(Session session)
        {
            await transitionGate.Task;
            return await session.StartProcessingAsync(createdId, 1);
        }

        var transitionOne = Transition(firstTransition);
        var transitionTwo = Transition(secondTransition);
        transitionGate.SetResult();
        var transitioned = await Task.WhenAll(transitionOne, transitionTwo);
        Assert.Single(transitioned, result => !result.IsReplay);
        Assert.Single(transitioned, result => result.IsReplay);

        await using var verify = CreateDb(database.ConnectionString);
        var period = await verify.PayrollPeriods.SingleAsync();
        Assert.Equal("Processing", period.Status);
        Assert.Equal(2, period.Version);
        Assert.Single(await verify.AuditEvents.Where(row =>
            row.ActionType == "payroll.period.created").ToListAsync());
        Assert.Single(await verify.AuditEvents.Where(row =>
            row.ActionType == "payroll.period.processing_started").ToListAsync());
    }

    private static RetailPOSDbContext CreateDb(string connectionString) =>
        new(new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseNpgsql(connectionString).Options);

    private sealed class Session : IAsyncDisposable
    {
        private readonly RetailPOSDbContext db;
        private readonly IRequiredAuditTransaction audit;
        private readonly PayrollPeriodStore periods;

        public Session(string connectionString)
        {
            var auditContext = new AuditContext();
            auditContext.Initialize(new AuditContextSnapshot(Guid.NewGuid(), 101, 101, 5, 7, null, 1,
                AuditSource.API, "POST", "/api/payroll/v1/periods", null, null, null,
                false, null, null, null, null, "actor", "Payroll"));
            db = new RetailPOSDbContext(new DbContextOptionsBuilder<RetailPOSDbContext>()
                .UseNpgsql(connectionString)
                .AddInterceptors(new AuditChangeTrackerInterceptor(auditContext,
                    NullLogger<AuditChangeTrackerInterceptor>.Instance))
                .Options);
            audit = new RequiredAuditTransaction(db, auditContext,
                new AuditService(db, auditContext, Mock.Of<IServiceProvider>(),
                    NullLogger<AuditService>.Instance),
                NullLogger<RequiredAuditTransaction>.Instance);
            periods = new PayrollPeriodStore(db);
        }

        public Task<PayrollPeriodResponse> CreateAsync(CreatePayrollPeriodRequest request) =>
            audit.ExecuteAsync(
                new RequiredAuditOperation(1, "payroll.period.created", "Payroll",
                    "Created a Payroll period.", "PayrollPeriod", "new"),
                token => periods.CreateAsync(1, 101, request, token),
                CancellationToken.None);

        public Task<PayrollPeriodResponse> StartProcessingAsync(long id, long expectedVersion) =>
            audit.ExecuteAsync(
                new RequiredAuditOperation(1, "payroll.period.processing_started", "Payroll",
                    "Started Payroll period processing.", "PayrollPeriod", id.ToString()),
                token => periods.StartProcessingAsync(1, 101, id, expectedVersion, token),
                result => !result.IsReplay,
                CancellationToken.None);

        public ValueTask DisposeAsync() => db.DisposeAsync();
    }
}
