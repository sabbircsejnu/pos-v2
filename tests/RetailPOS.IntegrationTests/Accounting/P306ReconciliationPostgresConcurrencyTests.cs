using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Npgsql;
using RetailPOS.Application.BankReconciliation.Abstractions;
using RetailPOS.Infrastructure.BankReconciliation;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Accounting;

public sealed class P306ReconciliationPostgresConcurrencyTests
{
    static P306ReconciliationPostgresConcurrencyTests() =>
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

    [Fact]
    public async Task ConcurrentMatchAfterBothPreflightReadsCreatesOneMatchAndActiveLineSet()
    {
        await PostgresTestDatabase.RunAsync(
            ResolveBaseConnectionString(),
            PostgresTestDatabasePrefix.ReconciliationMatch,
            async database =>
            {
                await using (var setup = CreateContext(database.ConnectionString))
                {
                    await setup.Database.EnsureCreatedAsync();
                    Seed(setup);
                    await setup.SaveChangesAsync();
                }

                var barrier = new MatchPreflightBarrier();

                async Task<Exception?> MatchAsync(long actorUserId)
                {
                    await using var context = CreateContext(
                        database.ConnectionString,
                        barrier);
                    var store = new BankReconciliationMatchCommandStore(context);
                    try
                    {
                        await store.CreateAsync(
                            1,
                            actorUserId,
                            new CreateBankReconciliationMatchInput(
                                700,
                                [1001],
                                [2001]),
                            CancellationToken.None);
                        return null;
                    }
                    catch (Exception exception)
                    {
                        return exception;
                    }
                }

                var outcomes = await Task.WhenAll(MatchAsync(9), MatchAsync(10));
                Assert.Single(outcomes, outcome => outcome is null);
                var failure = Assert.IsType<DbUpdateException>(
                    Assert.Single(outcomes, outcome => outcome is not null));
                var postgresFailure = Assert.IsType<PostgresException>(failure.InnerException);
                Assert.Equal(PostgresErrorCodes.UniqueViolation, postgresFailure.SqlState);

                await using var verify = CreateContext(database.ConnectionString);
                var match = await verify.BankReconciliationMatches
                    .Include(row => row.Lines)
                    .SingleAsync(row => row.BankReconciliationSessionId == 700);
                Assert.Equal(BankReconciliationMatchStatuses.Effective, match.Status);
                Assert.Contains(match.CreatedBy, new long[] { 9, 10 });
                Assert.Equal(2, match.Lines.Count);
                Assert.All(match.Lines, line => Assert.True(line.IsActive));
                Assert.Single(match.Lines, line => line.BankStatementLineId == 1001);
                Assert.Single(match.Lines, line => line.JournalEntryLineId == 2001);
                var session = await verify.BankReconciliationSessions
                    .SingleAsync(row => row.Id == 700);
                Assert.Equal(BankReconciliationSessionStatuses.InProgress, session.Status);
                Assert.Equal(match.CreatedBy, session.UpdatedBy);
            });
    }

    private static RetailPOSDbContext CreateContext(
        string connectionString,
        DbCommandInterceptor? interceptor = null)
    {
        var options = new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseNpgsql(connectionString);
        if (interceptor is not null)
            options.AddInterceptors(interceptor);
        return new RetailPOSDbContext(options.Options);
    }

    private static void Seed(RetailPOSDbContext db)
    {
        db.Businesses.Add(new Business { Id = 1, Name = "Reconciliation concurrency" });
        db.ChartAccounts.Add(new ChartAccount
        {
            Id = 101,
            BusinessId = 1,
            AccountCode = "101",
            AccountName = "Bank",
            AccountType = ChartAccountTypes.Asset,
            NormalBalanceType = NormalBalanceTypes.Debit,
            IsActive = true,
            AllowManualPosting = true
        });
        db.BankAccounts.Add(new BankAccount
        {
            Id = 501,
            BusinessId = 1,
            ChartAccountId = 101,
            DisplayName = "Bank"
        });
        db.BankReconciliationSessions.Add(new BankReconciliationSession
        {
            Id = 700,
            BusinessId = 1,
            BankAccountId = 501,
            PeriodStart = new DateTime(2026, 9, 1),
            PeriodEnd = new DateTime(2026, 9, 30),
            Status = BankReconciliationSessionStatuses.Open
        });
        db.BankStatementImports.Add(new BankStatementImport
        {
            Id = 100,
            BusinessId = 1,
            BankAccountId = 501,
            SourceFileName = "statement.csv",
            SourceFileChecksum = "statement-checksum",
            Status = BankStatementImportStatuses.Imported
        });
        db.BankStatementLines.Add(new BankStatementLine
        {
            Id = 1001,
            BusinessId = 1,
            BankAccountId = 501,
            BankStatementImportId = 100,
            SourceRowNumber = 1,
            TransactionDate = new DateTime(2026, 9, 10),
            Description = "Deposit",
            SignedAmount = 10,
            CreditAmount = 10
        });
        db.JournalEntries.Add(new JournalEntry
        {
            Id = 200,
            BusinessId = 1,
            JournalNumber = "JR-1",
            JournalType = "Sale",
            ReferenceType = "Sale",
            ReferenceId = "1",
            Status = JournalEntryStatuses.Posted,
            PostingDate = new DateTime(2026, 9, 10),
            TransactionDate = new DateTime(2026, 9, 10),
            BusinessDate = new DateTime(2026, 9, 10),
            Lines =
            [
                new JournalEntryLine
                {
                    Id = 2001,
                    AccountId = 101,
                    DebitAmount = 10,
                    CreditAmount = 0,
                    Description = "Deposit"
                }
            ]
        });
    }

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

    private sealed class MatchPreflightBarrier : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _bothReadersReady =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains(
                    "SELECT EXISTS",
                    StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains(
                    "bank_reconciliation_match_lines",
                    StringComparison.OrdinalIgnoreCase))
            {
                if (Interlocked.Increment(ref _arrivals) == 2)
                    _bothReadersReady.TrySetResult();
                await _bothReadersReady.Task.WaitAsync(cancellationToken);
            }

            return await base.ReaderExecutedAsync(
                command,
                eventData,
                result,
                cancellationToken);
        }
    }
}
