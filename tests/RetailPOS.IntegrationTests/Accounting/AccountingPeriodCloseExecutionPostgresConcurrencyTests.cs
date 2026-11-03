using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Moq;
using Npgsql;
using RetailPOS.Application.AccountingCloseRuns.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.AccountingCloseRuns;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Accounting;

public sealed class AccountingPeriodCloseExecutionPostgresConcurrencyTests
{
    [Fact]
    public async Task ConcurrentExecution_AfterBothApprovedRunReadsCommitsOneCloseAndOneAudit()
    {
        await PostgresTestDatabase.RunAsync(
            ResolveBaseConnectionString(),
            PostgresTestDatabasePrefix.PeriodClosePrepare,
            async database =>
            {
                var now = DateTime.UtcNow;
                await using (var setup = CreateContext(database.ConnectionString))
                {
                    await setup.Database.EnsureCreatedAsync();
                    setup.Businesses.Add(new Business
                    {
                        Id = 1,
                        Name = "Close execution race"
                    });
                    setup.FinancialYears.Add(new FinancialYear
                    {
                        Id = 1,
                        BusinessId = 1,
                        YearCode = "FY26",
                        Name = "FY26",
                        StartDate = UtcDate(2026, 1, 1),
                        EndDate = UtcDate(2026, 12, 31),
                        IsCurrent = true
                    });
                    setup.AccountingPeriods.Add(new AccountingPeriod
                    {
                        Id = 10,
                        FinancialYearId = 1,
                        PeriodNumber = 1,
                        Name = "January",
                        StartDate = UtcDate(2026, 1, 1),
                        EndDate = UtcDate(2026, 1, 31),
                        Status = AccountingPeriodStatuses.Locked,
                        LockedAt = now,
                        LockedBy = 100,
                        StatusReason = "Close run prepared"
                    });
                    setup.AccountingPeriodCloseRuns.Add(new AccountingPeriodCloseRun
                    {
                        Id = 10,
                        BusinessId = 1,
                        AccountingPeriodId = 10,
                        RunNumber = 1,
                        Status = AccountingPeriodCloseRunStatuses.Approved,
                        EvaluationSequence = 1,
                        PreparedBy = 100,
                        PreparedAt = now,
                        ApprovedBy = 101,
                        ApprovedAt = now,
                        CreatedBy = 100,
                        CreatedAt = now
                    });
                    await setup.SaveChangesAsync();
                }

                var barrier = new CloseRunReadBarrier();
                var interceptor = new CloseRunReadBarrierInterceptor(barrier);
                var audit = new Mock<IAuditService>();
                audit.Setup(service => service.RecordAsync(
                        It.IsAny<AuditEventInput>(),
                        It.IsAny<CancellationToken>()))
                    .Returns(Task.CompletedTask);

                async Task<Exception?> ExecuteAsync(long actorUserId)
                {
                    try
                    {
                        await using var context = CreateContext(
                            database.ConnectionString,
                            interceptor);
                        var store = new AccountingPeriodCloseRunCommandStore(
                            context,
                            new PassingFinancialGateEvaluator(),
                            audit.Object);
                        await store.ExecuteAsync(
                            1,
                            10,
                            "Concurrent close",
                            actorUserId,
                            CancellationToken.None);
                        return null;
                    }
                    catch (Exception exception)
                    {
                        return exception;
                    }
                }

                var outcomes = await Task.WhenAll(
                    ExecuteAsync(102),
                    ExecuteAsync(103));

                Assert.Equal(2, barrier.Arrivals);
                Assert.Single(outcomes, outcome => outcome is null);
                var failure = Assert.Single(outcomes, outcome => outcome is not null);
                var updateFailure = Assert.IsType<DbUpdateException>(failure);
                var postgresFailure = Assert.IsType<PostgresException>(
                    updateFailure.InnerException);
                Assert.Equal(PostgresErrorCodes.UniqueViolation, postgresFailure.SqlState);

                await using var verify = CreateContext(database.ConnectionString);
                var run = await verify.AccountingPeriodCloseRuns
                    .SingleAsync(value => value.Id == 10);
                var period = await verify.AccountingPeriods
                    .SingleAsync(value => value.Id == 10);
                Assert.Equal(AccountingPeriodCloseRunStatuses.Executed, run.Status);
                Assert.Equal(2, run.EvaluationSequence);
                Assert.Contains(run.ExecutedBy, new long?[] { 102, 103 });
                Assert.Equal(AccountingPeriodStatuses.Closed, period.Status);
                Assert.Equal("Concurrent close", period.StatusReason);
                var snapshots = await verify.AccountingCloseGateSnapshots
                    .Where(snapshot => snapshot.AccountingPeriodCloseRunId == run.Id
                        && snapshot.EvaluationSequence == 2)
                    .ToListAsync();
                Assert.Equal(5, snapshots.Count);
                Assert.Equal(
                    snapshots.Count,
                    snapshots.Select(snapshot => snapshot.GateCode)
                        .Distinct(StringComparer.Ordinal)
                        .Count());
                audit.Verify(
                    service => service.RecordAsync(
                        It.Is<AuditEventInput>(input =>
                            input.Summary == "Accounting period close executed"),
                        It.IsAny<CancellationToken>()),
                    Times.Once);
            });
    }

    private static RetailPOSDbContext CreateContext(
        string connectionString,
        params IInterceptor[] interceptors) =>
        new(new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseNpgsql(connectionString)
            .AddInterceptors(interceptors)
            .Options);

    private static DateTime UtcDate(int year, int month, int day) =>
        new(year, month, day, 0, 0, 0, DateTimeKind.Utc);

    private static string ResolveBaseConnectionString()
    {
        var configured = Environment.GetEnvironmentVariable("RETAILPOS_TEST_POSTGRES");
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        var settingsPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "RetailPOS.API", "appsettings.json"));
        return new ConfigurationBuilder()
            .AddJsonFile(settingsPath, optional: false)
            .Build()
            .GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "DefaultConnection is not configured for PostgreSQL integration tests.");
    }

    private sealed class PassingFinancialGateEvaluator
        : IAccountingPeriodCloseFinancialGateEvaluator
    {
        public Task<IReadOnlyList<AccountingPeriodCloseFinancialGateResult>> EvaluateAsync(
            DateTime periodStart,
            DateTime periodEnd,
            long financialYearId,
            long accountingPeriodId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AccountingPeriodCloseFinancialGateResult>>(
            [
                new AccountingPeriodCloseFinancialGateResult(
                    "configuration",
                    AccountingCloseGateStatuses.Passed,
                    0,
                    "Required accounting mappings are valid.")
            ]);
    }

    private sealed class CloseRunReadBarrier
    {
        private readonly TaskCompletionSource _bothReadersReady =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;

        public int Arrivals => Math.Min(2, Volatile.Read(ref _arrivals));

        public async Task WaitAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _arrivals) == 2)
                _bothReadersReady.TrySetResult();
            await _bothReadersReady.Task.WaitAsync(
                TimeSpan.FromSeconds(20),
                cancellationToken);
        }
    }

    private sealed class CloseRunReadBarrierInterceptor(
        CloseRunReadBarrier barrier) : DbCommandInterceptor
    {
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains(
                "accounting_period_close_runs",
                StringComparison.OrdinalIgnoreCase))
            {
                await barrier.WaitAsync(cancellationToken);
            }

            return await base.ReaderExecutedAsync(
                command,
                eventData,
                result,
                cancellationToken);
        }
    }
}
