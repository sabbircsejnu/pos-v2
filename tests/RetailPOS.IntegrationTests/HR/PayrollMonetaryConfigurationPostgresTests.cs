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

public sealed class PayrollMonetaryConfigurationPostgresTests
{
    [Fact]
    public async Task ConcurrentConfigurationCreationHasOneAuditedOutcome()
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        var settingsPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "RetailPOS.API", "appsettings.json"));
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(settingsPath, optional: false).Build();
        await using var database = await PostgresTestDatabase.CreateAsync(
            Environment.GetEnvironmentVariable("RETAILPOS_TEST_POSTGRES")
                ?? configuration.GetConnectionString("DefaultConnection")!,
            PostgresTestDatabasePrefix.MigrationBaseline);
        var options = new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseNpgsql(database.ConnectionString).Options;
        await using (var setup = new RetailPOSDbContext(options))
        {
            Assert.False(setup.Database.HasPendingModelChanges());
            await setup.Database.MigrateAsync();
            setup.Businesses.Add(new Business { Id = 1, Name = "Payroll monetary configuration" });
            await setup.SaveChangesAsync();
        }

        var request = new CreatePayrollMonetaryConfigurationRequest(
            new(2026, 10, 1), "USD", 2, MidpointRounding.AwayFromZero.ToString());
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<(PayrollMonetaryConfigurationResponse? Result, Exception? Error)> CreateAsync()
        {
            await using var session = new MonetarySession(options);
            await gate.Task;
            try
            {
                var result = await session.Audit.ExecuteAsync(
                    new RequiredAuditOperation(1, "payroll.monetary_configuration.created",
                        "Payroll", "Created an immutable Business-scoped Payroll monetary configuration.",
                        "PayrollMonetaryConfigurationVersion", "2026-10-01"),
                    token => session.Store.CreateAsync(1, 101, request, token),
                    default);
                return (result, null);
            }
            catch (Exception exception)
            {
                return (null, exception);
            }
        }

        var first = CreateAsync();
        var second = CreateAsync();
        gate.SetResult();
        var outcomes = await Task.WhenAll(first, second);
        Assert.Single(outcomes, row => row.Result is not null);
        Assert.Single(outcomes, row => row.Error is PayrollMonetaryConfigurationConflictException);

        await using var verify = new RetailPOSDbContext(options);
        Assert.Single(await verify.PayrollMonetaryConfigurationVersions.ToArrayAsync());
        Assert.Single(await verify.AuditEvents.Where(row =>
            row.ActionType == "payroll.monetary_configuration.created").ToArrayAsync());
    }

    private sealed class MonetarySession : IAsyncDisposable
    {
        private readonly RetailPOSDbContext db;

        public IPayrollMonetaryConfigurationStore Store { get; }
        public IRequiredAuditTransaction Audit { get; }

        public MonetarySession(DbContextOptions<RetailPOSDbContext> options)
        {
            var context = new AuditContext();
            context.Initialize(new AuditContextSnapshot(Guid.NewGuid(), 101, 101, 5, 7, null, 1,
                AuditSource.API, "POST", "/api/payroll/v1/monetary-configurations",
                null, null, null, false, null, null, null, null, "actor", "Payroll"));
            db = new RetailPOSDbContext(options);
            Store = new PayrollMonetaryConfigurationStore(db);
            Audit = new RequiredAuditTransaction(db, context,
                new AuditService(db, context, Mock.Of<IServiceProvider>(),
                    NullLogger<AuditService>.Instance),
                NullLogger<RequiredAuditTransaction>.Instance);
        }

        public ValueTask DisposeAsync() => db.DisposeAsync();
    }
}
