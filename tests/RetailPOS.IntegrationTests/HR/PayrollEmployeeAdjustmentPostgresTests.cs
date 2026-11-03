using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.HR.Payroll;
using RetailPOS.Core.Audit;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Core.Entities.HR.Payroll;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.AuditEvents;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.HR.Payroll;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.IntegrationTests.HR;

public sealed class PayrollEmployeeAdjustmentPostgresTests
{
    [Fact]
    public async Task ConcurrentSameKeyCreatesOneImmutableAdjustmentAndOneAuditEvent()
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        var configuration = new ConfigurationBuilder().AddJsonFile(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "RetailPOS.API", "appsettings.json"))).Build();
        await using var database = await PostgresTestDatabase.CreateAsync(
            Environment.GetEnvironmentVariable("RETAILPOS_TEST_POSTGRES")
            ?? configuration.GetConnectionString("DefaultConnection")!,
            PostgresTestDatabasePrefix.PayrollAdjustment);

        long salaryComponentId;
        await using (var setup = new RetailPOSDbContext(new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseNpgsql(database.ConnectionString).Options))
        {
            Assert.False(setup.Database.HasPendingModelChanges());
            var expectedMigrations = setup.Database.GetMigrations().ToArray();
            await setup.Database.MigrateAsync();
            Assert.Equal(expectedMigrations, await setup.Database.GetAppliedMigrationsAsync());
            await setup.Database.MigrateAsync();
            await HrRosterPersistenceTests.Seed(setup);
            var employee = await setup.HrEmployees.SingleAsync(row => row.BusinessId == 1 && row.Id == 1);
            employee.CurrentEmploymentStatus = "Active";
            var component = new PayrollSalaryComponent
            {
                BusinessId = 1,
                Code = "CONCURRENT_ALLOWANCE",
                Name = "Concurrent allowance",
                ComponentType = "Earning",
                ValueType = "Fixed",
                IsRecurring = true
            };
            setup.PayrollSalaryComponents.Add(component);
            await setup.SaveChangesAsync();
            salaryComponentId = component.Id;
        }

        var request = new SavePayrollEmployeeAdjustmentRequest(
            null, null, 1, salaryComponentId, "RecurringAllowance", "Manual",
            100m, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1), null,
            "Concurrent request", null, "concurrent-adjustment-key");
        await using var first = new Session(database.ConnectionString, request);
        await using var second = new Session(database.ConnectionString, request);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<PayrollEmployeeAdjustmentResponse> Save(Session session)
        {
            await gate.Task;
            return await session.SaveAsync();
        }

        var firstSave = Save(first);
        var secondSave = Save(second);
        gate.SetResult();
        var responses = await Task.WhenAll(firstSave, secondSave);

        Assert.Single(responses.Select(response => response.Id).Distinct());
        Assert.Single(responses, response => !response.IsReplay);
        Assert.Single(responses, response => response.IsReplay);
        await using var verify = new RetailPOSDbContext(new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseNpgsql(database.ConnectionString).Options);
        Assert.Single(await verify.PayrollEmployeeAdjustmentVersions
            .Where(row => row.BusinessId == 1 && row.EmployeeId == 1).ToListAsync());
        Assert.Single(await verify.AuditEvents
            .Where(row => row.ActionType == "payroll.allowance_deduction.saved").ToListAsync());
    }

    private sealed class Session : IAsyncDisposable
    {
        private readonly RetailPOSDbContext db;
        private readonly IRequiredAuditTransaction audit;
        private readonly PayrollEmployeeAdjustmentStore store;
        private readonly SavePayrollEmployeeAdjustmentRequest request;

        public Session(string connectionString, SavePayrollEmployeeAdjustmentRequest request)
        {
            this.request = request;
            var auditContext = new AuditContext();
            auditContext.Initialize(new AuditContextSnapshot(Guid.NewGuid(), 101, 101, 5, 7, null, 1,
                AuditSource.API, "POST", "/api/payroll/v1/adjustments", null, null, null,
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
            store = new PayrollEmployeeAdjustmentStore(db, new PayrollSalarySetupStore(db));
        }

        public Task<PayrollEmployeeAdjustmentResponse> SaveAsync() =>
            audit.ExecuteAsync(
                new RequiredAuditOperation(1, "payroll.allowance_deduction.saved", "Payroll",
                    "Created a PostgreSQL concurrency-test adjustment.",
                    "PayrollEmployeeAdjustmentVersion", "new"),
                token => store.SaveAsync(1, 101, request, token),
                response => !response.IsReplay,
                CancellationToken.None);

        public ValueTask DisposeAsync() => db.DisposeAsync();
    }
}
