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

public sealed class PayrollEmployeeLoanPostgresConcurrencyTests
{
    [Fact]
    public async Task ConcurrentSameKeyCreatesOneLoanAndConcurrentRepaymentsCannotOverdraw()
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
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var recoveryStartDate = today < new DateOnly(2026, 1, 1)
            ? new DateOnly(2026, 1, 1)
            : today;
        long employeeId;

        await using (var setup = CreateDb(database.ConnectionString))
        {
            Assert.False(setup.Database.HasPendingModelChanges());
            await setup.Database.MigrateAsync();
            await HrRosterPersistenceTests.Seed(setup);
            employeeId = await setup.HrEmployees.Where(row => row.BusinessId == 1)
                .Select(row => row.Id).SingleAsync();
            setup.PayrollEmployeeLoans.Add(new PayrollEmployeeLoan
            {
                BusinessId = 1,
                EmployeeId = employeeId,
                OperationalLocationProjectionId = 10,
                LoanType = "Loan",
                PrincipalAmount = 100m,
                ApprovedAmount = 100m,
                DisbursedDate = today,
                RecoveryStartDate = recoveryStartDate,
                RecoveryMode = "Manual",
                OutstandingAmount = 100m,
                ProposedScheduleJson = "[]",
                RequestHash = new string('a', 64),
                IdempotencyKey = "seed-active-loan",
                IdempotencyPayloadHash = new string('b', 64),
                Reason = "PostgreSQL concurrency test",
                Status = "Active",
                CreatedByUserId = 101,
                CreatedAtUtc = DateTime.UtcNow,
                Version = 1
            });
            await setup.SaveChangesAsync();
        }

        var createRequest = new CreatePayrollEmployeeLoanRequest(
            employeeId, "Advance", 200m, recoveryStartDate, "Manual", null, [],
            "Concurrent advance", null, "concurrent-loan-key");
        await using var firstCreate = new Session(database.ConnectionString);
        await using var secondCreate = new Session(database.ConnectionString);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<PayrollEmployeeLoanResponse> Create(Session session)
        {
            await gate.Task;
            return await session.CreateAsync(createRequest);
        }

        var createOne = Create(firstCreate);
        var createTwo = Create(secondCreate);
        gate.SetResult();
        var created = await Task.WhenAll(createOne, createTwo);
        Assert.Single(created.Select(row => row.Id).Distinct());
        Assert.Single(created, row => !row.IsReplay);
        Assert.Single(created, row => row.IsReplay);

        var repayment = new RecordPayrollLoanRepaymentRequest(
            75m, today, "Concurrent repayment", null, "repayment-key");
        await using var firstRepayment = new Session(database.ConnectionString);
        await using var secondRepayment = new Session(database.ConnectionString);
        var repaymentGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<Exception?> Repay(Session session, long loanId, string key)
        {
            await repaymentGate.Task;
            try
            {
                await session.RepayAsync(loanId, repayment with { IdempotencyKey = key });
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        var repaymentOne = Repay(firstRepayment, 1, "repayment-key-a");
        var repaymentTwo = Repay(secondRepayment, 1, "repayment-key-b");
        repaymentGate.SetResult();
        var repaymentResults = await Task.WhenAll(repaymentOne, repaymentTwo);
        Assert.Single(repaymentResults, exception => exception is null);
        Assert.Single(repaymentResults, exception => exception is ArgumentException);

        await using var verify = CreateDb(database.ConnectionString);
        var activeLoan = await verify.PayrollEmployeeLoans.SingleAsync(row =>
            row.BusinessId == 1 && row.Id == 1);
        Assert.Equal(25m, activeLoan.OutstandingAmount);
        Assert.Single(await verify.PayrollLoanRepayments.Where(row =>
            row.BusinessId == 1 && row.LoanId == 1).ToArrayAsync());
        Assert.Single(await verify.PayrollEmployeeLoans.Where(row =>
            row.BusinessId == 1 && row.EmployeeId == employeeId
            && row.IdempotencyKey == createRequest.IdempotencyKey).ToArrayAsync());
        Assert.Single(await verify.AuditEvents.Where(row =>
            row.ActionType == "payroll.loan.created").ToArrayAsync());
        Assert.Single(await verify.AuditEvents.Where(row =>
            row.ActionType == "payroll.loan.repayment_recorded").ToArrayAsync());
    }

    private static RetailPOSDbContext CreateDb(string connectionString) =>
        new(new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseNpgsql(connectionString).Options);

    private sealed class Session : IAsyncDisposable
    {
        private readonly RetailPOSDbContext db;
        private readonly IRequiredAuditTransaction audit;
        private readonly PayrollEmployeeLoanStore loans;

        public Session(string connectionString)
        {
            var auditContext = new AuditContext();
            auditContext.Initialize(new AuditContextSnapshot(Guid.NewGuid(), 101, 101, 5, 7, null, 1,
                AuditSource.API, "POST", "/api/payroll/v1/loans", null, null, null,
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
            loans = new PayrollEmployeeLoanStore(db, new PayrollSalarySetupStore(db));
        }

        public Task<PayrollEmployeeLoanResponse> CreateAsync(CreatePayrollEmployeeLoanRequest request) =>
            audit.ExecuteAsync(
                new RequiredAuditOperation(1, "payroll.loan.created", "Payroll",
                    "Created a PostgreSQL concurrency-test loan.", "PayrollEmployeeLoan", "new"),
                token => loans.CreateAsync(1, 101, request, token),
                response => !response.IsReplay,
                CancellationToken.None);

        public Task<PayrollEmployeeLoanResponse> RepayAsync(
            long loanId, RecordPayrollLoanRepaymentRequest request) =>
            audit.ExecuteAsync(
                new RequiredAuditOperation(1, "payroll.loan.repayment_recorded", "Payroll",
                    "Recorded a PostgreSQL concurrency-test repayment.",
                    "PayrollEmployeeLoan", loanId.ToString()),
                token => loans.RecordRepaymentAsync(1, 101, loanId, request, token),
                response => !response.IsReplay,
                CancellationToken.None);

        public ValueTask DisposeAsync() => db.DisposeAsync();
    }
}
