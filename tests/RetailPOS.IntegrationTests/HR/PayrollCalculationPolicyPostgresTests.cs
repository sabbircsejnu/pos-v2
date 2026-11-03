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

public sealed class PayrollCalculationPolicyPostgresTests
{
    [Fact]
    public async Task ConcurrentOverlappingPolicyCreationHasOneAuditedOutcome()
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        var configuration = new ConfigurationBuilder().AddJsonFile(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "RetailPOS.API",
            "appsettings.json"))).Build();
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
            setup.Businesses.Add(new Business { Id = 1, Name = "Payroll policy concurrency" });
            await setup.SaveChangesAsync();
        }

        var request = new CreatePayrollCalculationPolicyRequest(
            new(2026, 10, 1), new(2026, 10, 31), "Fixed30", [],
            false, false, null, null, null, 30, 0,
            null, null, null, null, null, null, null, null, null, null);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<bool> CreatePolicyAsync()
        {
            await using var session = new PolicySession(options);
            await gate.Task;
            try
            {
                await session.Audit.ExecuteAsync(
                    new RequiredAuditOperation(1, "payroll.calculation_policy.created", "Payroll",
                        "Created an immutable Payroll calculation policy version.",
                        "PayrollCalculationPolicyVersion", "2026-10-01"),
                    token => session.Store.CreateAsync(1, 101, request, token),
                    default);
                return true;
            }
            catch (PayrollCalculationPolicyConflictException)
            {
                return false;
            }
        }

        var first = CreatePolicyAsync();
        var second = CreatePolicyAsync();
        gate.SetResult();
        Assert.Single(await Task.WhenAll(first, second), outcome => outcome);

        await using var verify = new RetailPOSDbContext(options);
        Assert.Single(await verify.PayrollCalculationPolicyVersions.ToArrayAsync());
        Assert.Single(await verify.AuditEvents.Where(row =>
            row.ActionType == "payroll.calculation_policy.created").ToArrayAsync());
    }

    private sealed class PolicySession : IAsyncDisposable
    {
        private readonly RetailPOSDbContext db;

        public IPayrollCalculationPolicyStore Store { get; }
        public IRequiredAuditTransaction Audit { get; }

        public PolicySession(DbContextOptions<RetailPOSDbContext> options)
        {
            var context = new AuditContext();
            context.Initialize(new AuditContextSnapshot(Guid.NewGuid(), 101, 101, 5, 7, null, 1,
                AuditSource.API, "POST", "/api/payroll/v1/calculation-policies",
                null, null, null, false, null, null, null, null, "actor", "Payroll"));
            db = new RetailPOSDbContext(options);
            Store = new PayrollCalculationPolicyStore(db);
            Audit = new RequiredAuditTransaction(db, context,
                new AuditService(db, context, Mock.Of<IServiceProvider>(),
                    NullLogger<AuditService>.Instance),
                NullLogger<RequiredAuditTransaction>.Instance);
        }

        public ValueTask DisposeAsync() => db.DisposeAsync();
    }
}
