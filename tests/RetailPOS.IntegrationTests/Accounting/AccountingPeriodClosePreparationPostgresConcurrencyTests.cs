using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Moq;
using RetailPOS.Application.AccountingCloseRuns.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.AccountingCloseRuns;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Accounting;

public sealed class AccountingPeriodClosePreparationPostgresConcurrencyTests
{
    [Fact]
    public async Task ConcurrentPreparation_AfterBothActiveRunReadsReturnsOneRunAndOneAudit()
    {
        await PostgresTestDatabase.RunAsync(
            ResolveBaseConnectionString(),
            PostgresTestDatabasePrefix.PeriodClosePrepare,
            PostgresTestDatabaseStrategy.FreshMigration,
            async database =>
            {
                await using (var setup = CreateContext(database.ConnectionString))
                {
                    await setup.Database.EnsureCreatedAsync();
                    setup.Businesses.Add(new Business { Id = 1, Name = "Close preparation race" });
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
                        EndDate = UtcDate(2026, 1, 31)
                    });
                    await setup.SaveChangesAsync();
                }

                var barrier = new ActiveRunReadBarrier();
                var interceptor = new ActiveRunReadBarrierInterceptor(barrier);
                var audit = new Mock<IAuditService>();
                var prepareAuditCount = 0;
                audit.Setup(service => service.RecordAsync(
                        It.IsAny<AuditEventInput>(),
                        It.IsAny<CancellationToken>()))
                    .Callback<AuditEventInput, CancellationToken>((input, _) =>
                    {
                        if (input.Summary == "Accounting period close prepared")
                            Interlocked.Increment(ref prepareAuditCount);
                    })
                    .Returns(Task.CompletedTask);

                async Task<long> PrepareAsync(long actorUserId)
                {
                    await using var context = CreateContext(
                        database.ConnectionString,
                        interceptor);
                    var store = new AccountingPeriodCloseRunCommandStore(
                        context,
                        new PassingFinancialGateEvaluator(),
                        audit.Object);
                    return await store.PrepareAsync(
                        1,
                        10,
                        actorUserId,
                        CancellationToken.None);
                }

                var runIds = await Task.WhenAll(
                    PrepareAsync(101),
                    PrepareAsync(102));

                Assert.Equal(runIds[0], runIds[1]);
                Assert.Equal(2, barrier.Arrivals);
                Assert.Equal(1, Volatile.Read(ref prepareAuditCount));

                await using var verify = CreateContext(database.ConnectionString);
                var run = await verify.AccountingPeriodCloseRuns
                    .SingleAsync(value => value.AccountingPeriodId == 10);
                Assert.Equal(runIds[0], run.Id);
                Assert.Equal(AccountingPeriodCloseRunStatuses.Prepared, run.Status);
                Assert.Equal(1, run.RunNumber);
                Assert.Contains(run.PreparedBy, new long[] { 101, 102 });
                Assert.Equal(
                    AccountingPeriodStatuses.Locked,
                    (await verify.AccountingPeriods.SingleAsync(value => value.Id == 10))
                    .Status);
                Assert.NotEmpty(await verify.AccountingCloseGateSnapshots
                    .Where(snapshot => snapshot.AccountingPeriodCloseRunId == run.Id)
                    .ToListAsync());
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

    private sealed class ActiveRunReadBarrier
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

    private sealed class ActiveRunReadBarrierInterceptor(
        ActiveRunReadBarrier barrier) : DbCommandInterceptor
    {
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains(
                    "accounting_period_close_runs",
                    StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains(
                    "status",
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
