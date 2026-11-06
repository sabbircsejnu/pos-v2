using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Moq;
using Npgsql;
using RetailPOS.Application.FinancialYearCloseRuns.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.FinancialYearCloseRuns;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Accounting;

public sealed class AccountingFinancialYearClosePreparationPostgresConcurrencyTests
{
    [Fact]
    public async Task ConcurrentPreparation_AfterBothActiveRunReadsLeavesOneRunAndOneAudit()
    {
        await PostgresTestDatabase.RunAsync(
            ResolveBaseConnectionString(),
            PostgresTestDatabasePrefix.FinancialYearClosePrepare,
            PostgresTestDatabaseStrategy.FreshMigration,
            async database =>
            {
                await using (var setup = CreateContext(database.ConnectionString))
                {
                    await setup.Database.EnsureCreatedAsync();
                    setup.Businesses.Add(new Business
                    {
                        Id = 1,
                        Name = "Financial year close race",
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
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
                        PeriodNumber = 12,
                        Name = "December",
                        StartDate = UtcDate(2026, 12, 1),
                        EndDate = UtcDate(2026, 12, 31),
                        Status = AccountingPeriodStatuses.Locked
                    });
                    setup.AccountingPeriodCloseRuns.Add(new AccountingPeriodCloseRun
                    {
                        Id = 100,
                        BusinessId = 1,
                        AccountingPeriodId = 10,
                        RunNumber = 1,
                        Status = AccountingPeriodCloseRunStatuses.Approved,
                        EvaluationSequence = 1,
                        PreparedBy = 1,
                        CreatedBy = 1
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
                        if (input.Summary == "Financial year close prepared")
                            Interlocked.Increment(ref prepareAuditCount);
                    })
                    .Returns(Task.CompletedTask);

                async Task<(long? Id, Exception? Error)> PrepareAsync(long actorUserId)
                {
                    await using var context = CreateContext(
                        database.ConnectionString,
                        interceptor);
                    var store = new AccountingFinancialYearCloseRunCommandStore(
                        context,
                        audit.Object,
                        Mock.Of<IAccountingFinancialYearCloseJournalPosting>());
                    try
                    {
                        return (
                            await store.PrepareAsync(
                                1,
                                1,
                                actorUserId,
                                CancellationToken.None),
                            null);
                    }
                    catch (Exception exception)
                    {
                        return (null, exception);
                    }
                }

                var outcomes = await Task.WhenAll(
                    PrepareAsync(101),
                    PrepareAsync(102));
                Assert.Equal(2, barrier.Arrivals);
                Assert.Single(outcomes, outcome => outcome.Id.HasValue);
                var error = Assert.IsType<DbUpdateException>(
                    Assert.Single(outcomes, outcome => outcome.Error is not null).Error);
                Assert.Equal(
                    PostgresErrorCodes.UniqueViolation,
                    Assert.IsType<PostgresException>(error.InnerException).SqlState);
                Assert.Equal(1, Volatile.Read(ref prepareAuditCount));

                await using var verify = CreateContext(database.ConnectionString);
                var run = await verify.AccountingFinancialYearCloseRuns.SingleAsync();
                Assert.Equal(1, run.RunNumber);
                Assert.Contains(run.PreparedBy, new long[] { 101, 102 });
                Assert.Equal(1, await verify.AccountingFinancialYearCloseRuns
                    .CountAsync(value => value.Status == AccountingFinancialYearCloseRunStatuses.Prepared));
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
                    "accounting_financial_year_close_runs",
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
