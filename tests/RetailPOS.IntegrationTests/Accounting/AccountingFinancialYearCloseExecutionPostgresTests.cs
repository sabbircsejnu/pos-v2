using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RetailPOS.Application.AccountingJournals.Abstractions;
using RetailPOS.Application.FinancialYearCloseRuns.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.AccountingJournals;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.FinancialYearCloseRuns;
using RetailPOS.Tests.Infrastructure;
using Moq;

namespace RetailPOS.Tests.Accounting;

public sealed class AccountingFinancialYearCloseExecutionPostgresTests
{
    [Fact]
    public async Task ExecutionFailureRollsBackJournalAndCloseState_ThenRetryCommitsOnce()
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
                    SeedApprovedRun(setup);
                    await setup.SaveChangesAsync();
                    await setup.Database.ExecuteSqlRawAsync(
                        """
                        CREATE FUNCTION fail_financial_year_close_execution() RETURNS trigger
                        LANGUAGE plpgsql AS $$
                        BEGIN
                            IF NEW.status = 'executed' THEN
                                RAISE EXCEPTION 'forced financial year close failure';
                            END IF;
                            RETURN NEW;
                        END;
                        $$;
                        CREATE TRIGGER fail_financial_year_close_execution
                        BEFORE UPDATE ON accounting_financial_year_close_runs
                        FOR EACH ROW EXECUTE FUNCTION fail_financial_year_close_execution();
                        """);
                }

                var audit = new Mock<IAuditService>();
                var executionAuditCount = 0;
                audit.Setup(service => service.RecordAsync(
                        It.IsAny<AuditEventInput>(),
                        It.IsAny<CancellationToken>()))
                    .Callback<AuditEventInput, CancellationToken>((input, _) =>
                    {
                        if (input.Summary == "Financial year close executed")
                            Interlocked.Increment(ref executionAuditCount);
                    })
                    .Returns(Task.CompletedTask);

                var failedPoster = new TestFinancialYearCloseJournalPosting(
                    CreateContext(database.ConnectionString));
                await using (failedPoster.Context)
                {
                    var failedStore = new AccountingFinancialYearCloseRunCommandStore(
                        failedPoster.Context,
                        audit.Object,
                        failedPoster);
                    await Assert.ThrowsAsync<DbUpdateException>(() =>
                        failedStore.ExecuteAsync(1, 200, 12, CancellationToken.None));
                }

                await using (var verifyRollback = CreateContext(database.ConnectionString))
                {
                    Assert.Equal(
                        AccountingFinancialYearCloseRunStatuses.Approved,
                        (await verifyRollback.AccountingFinancialYearCloseRuns
                            .SingleAsync(value => value.Id == 200)).Status);
                    Assert.Equal(
                        AccountingPeriodStatuses.Locked,
                        (await verifyRollback.AccountingPeriods
                            .SingleAsync(value => value.Id == 10)).Status);
                    Assert.Equal(
                        AccountingPeriodCloseRunStatuses.Approved,
                        (await verifyRollback.AccountingPeriodCloseRuns
                            .SingleAsync(value => value.Id == 100)).Status);
                    Assert.False((await verifyRollback.FinancialYears
                        .SingleAsync(value => value.Id == 1)).IsClosed);
                    Assert.False(await verifyRollback.JournalEntries.AnyAsync(
                        value => value.JournalType == AccountingJournalTypes.YearEndClosing));
                    await verifyRollback.Database.ExecuteSqlRawAsync(
                        """
                        DROP TRIGGER fail_financial_year_close_execution
                            ON accounting_financial_year_close_runs;
                        DROP FUNCTION fail_financial_year_close_execution();
                        """);
                }

                var successfulPoster = new TestFinancialYearCloseJournalPosting(
                    CreateContext(database.ConnectionString));
                await using (successfulPoster.Context)
                {
                    var store = new AccountingFinancialYearCloseRunCommandStore(
                        successfulPoster.Context,
                        audit.Object,
                        successfulPoster);
                    await store.ExecuteAsync(1, 200, 12, CancellationToken.None);
                }

                var closingDraft = Assert.Single(successfulPoster.Drafts);
                Assert.Equal(0m,
                    closingDraft.Lines.Sum(line => line.DebitAmount)
                    - closingDraft.Lines.Sum(line => line.CreditAmount));
                Assert.Contains(closingDraft.Lines, line =>
                    line.AccountId == 20 && line.DebitAmount == 100m);
                Assert.Contains(closingDraft.Lines, line =>
                    line.AccountId == 21 && line.CreditAmount == 40m);
                Assert.Contains(closingDraft.Lines, line =>
                    line.AccountId == 3 && line.CreditAmount == 60m);
                Assert.Contains(closingDraft.Lines, line =>
                    line.AccountId == 5 && line.CreditAmount == 20m);
                Assert.Contains(closingDraft.Lines, line =>
                    line.AccountId == 4 && line.DebitAmount == 20m);
                Assert.Equal(1, Volatile.Read(ref executionAuditCount));

                var duplicatePoster = new TestFinancialYearCloseJournalPosting(
                    CreateContext(database.ConnectionString));
                await using (duplicatePoster.Context)
                {
                    var duplicateStore = new AccountingFinancialYearCloseRunCommandStore(
                        duplicatePoster.Context,
                        audit.Object,
                        duplicatePoster);
                    await duplicateStore.ExecuteAsync(1, 200, 12, CancellationToken.None);
                }
                Assert.Empty(duplicatePoster.Drafts);
                Assert.Equal(1, Volatile.Read(ref executionAuditCount));

                await using var final = CreateContext(database.ConnectionString);
                Assert.Equal(
                    AccountingFinancialYearCloseRunStatuses.Executed,
                    (await final.AccountingFinancialYearCloseRuns
                        .SingleAsync(value => value.Id == 200)).Status);
                Assert.Equal(
                    AccountingPeriodStatuses.Closed,
                    (await final.AccountingPeriods
                        .SingleAsync(value => value.Id == 10)).Status);
                Assert.Equal(
                    AccountingPeriodCloseRunStatuses.Executed,
                    (await final.AccountingPeriodCloseRuns
                        .SingleAsync(value => value.Id == 100)).Status);
                Assert.True((await final.FinancialYears
                    .SingleAsync(value => value.Id == 1)).IsClosed);
                var executedRun = await final.AccountingFinancialYearCloseRuns
                    .SingleAsync(value => value.Id == 200);
                Assert.NotNull(executedRun.YearEndJournalEntryId);
                Assert.Equal(
                    JournalEntryStatuses.Posted,
                    (await final.JournalEntries.SingleAsync(
                        value => value.Id == executedRun.YearEndJournalEntryId)).Status);
            });
    }

    [Fact]
    public async Task ReopenFailureRollsBackJournalAndCloseState_ThenRetryCommitsOnce()
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
                    SeedApprovedRun(setup);
                    await setup.SaveChangesAsync();
                    var run = setup.AccountingFinancialYearCloseRuns.Single();
                    run.Status = AccountingFinancialYearCloseRunStatuses.Executed;
                    run.YearEndJournalEntryId = 900;
                    setup.FinancialYears.Single(value => value.Id == 1).IsClosed = true;
                    setup.AccountingPeriods.Single(value => value.Id == 10).Status =
                        AccountingPeriodStatuses.Closed;
                    setup.AccountingPeriodCloseRuns.Single(value => value.Id == 100).Status =
                        AccountingPeriodCloseRunStatuses.Executed;
                    setup.JournalEntries.Add(new JournalEntry
                    {
                        Id = 900,
                        BusinessId = 1,
                        FinancialYearId = 1,
                        AccountingPeriodId = 10,
                        JournalType = AccountingJournalTypes.YearEndClosing,
                        ReferenceType = AccountingReferenceTypes.FinancialYearClose,
                        ReferenceId = "1",
                        TransactionDate = UtcDate(2026, 12, 31),
                        PostingDate = UtcDate(2026, 12, 31),
                        BusinessDate = UtcDate(2026, 12, 31),
                        Status = JournalEntryStatuses.Posted,
                        IsSystemGenerated = true,
                        Lines =
                        [
                            new JournalEntryLine
                            {
                                AccountId = 3,
                                DebitAmount = 100m,
                                CreatedBy = 12,
                                CreatedAt = DateTime.UtcNow
                            },
                            new JournalEntryLine
                            {
                                AccountId = 20,
                                CreditAmount = 100m,
                                CreatedBy = 12,
                                CreatedAt = DateTime.UtcNow
                            }
                        ]
                    });
                    await setup.SaveChangesAsync();
                    await setup.Database.ExecuteSqlRawAsync(
                        """
                        CREATE FUNCTION fail_financial_year_close_reopen() RETURNS trigger
                        LANGUAGE plpgsql AS $$
                        BEGIN
                            IF NEW.status = 'reopened' THEN
                                RAISE EXCEPTION 'forced financial year reopen failure';
                            END IF;
                            RETURN NEW;
                        END;
                        $$;
                        CREATE TRIGGER fail_financial_year_close_reopen
                        BEFORE UPDATE ON accounting_financial_year_close_runs
                        FOR EACH ROW EXECUTE FUNCTION fail_financial_year_close_reopen();
                        """);
                }

                var audit = new Mock<IAuditService>();
                var reopenAuditCount = 0;
                audit.Setup(service => service.RecordAsync(
                        It.IsAny<AuditEventInput>(),
                        It.IsAny<CancellationToken>()))
                    .Callback<AuditEventInput, CancellationToken>((input, _) =>
                    {
                        if (input.Summary
                            == "Financial year close reopened with year-end reversal")
                        {
                            Interlocked.Increment(ref reopenAuditCount);
                        }
                    })
                    .Returns(Task.CompletedTask);
                var postingDate = UtcDate(2027, 1, 15);

                var failedPoster = new TestFinancialYearCloseJournalPosting(
                    CreateContext(database.ConnectionString));
                await using (failedPoster.Context)
                {
                    var store = new AccountingFinancialYearCloseRunCommandStore(
                        failedPoster.Context,
                        audit.Object,
                        failedPoster);
                    await Assert.ThrowsAsync<DbUpdateException>(() =>
                        store.ReopenAsync(
                            1,
                            200,
                            " correction ",
                            postingDate,
                            14,
                            CancellationToken.None));
                }

                await using (var verifyRollback = CreateContext(database.ConnectionString))
                {
                    Assert.Equal(
                        AccountingFinancialYearCloseRunStatuses.Executed,
                        (await verifyRollback.AccountingFinancialYearCloseRuns
                            .SingleAsync(value => value.Id == 200)).Status);
                    Assert.True((await verifyRollback.FinancialYears
                        .SingleAsync(value => value.Id == 1)).IsClosed);
                    Assert.Equal(
                        JournalEntryStatuses.Posted,
                        (await verifyRollback.JournalEntries
                            .SingleAsync(value => value.Id == 900)).Status);
                    Assert.False(await verifyRollback.JournalEntries
                        .AnyAsync(value => value.ReversalOfJournalEntryId == 900));
                    Assert.Equal(0, Volatile.Read(ref reopenAuditCount));
                    await verifyRollback.Database.ExecuteSqlRawAsync(
                        """
                        DROP TRIGGER fail_financial_year_close_reopen
                            ON accounting_financial_year_close_runs;
                        DROP FUNCTION fail_financial_year_close_reopen();
                        """);
                }

                var successfulPoster = new TestFinancialYearCloseJournalPosting(
                    CreateContext(database.ConnectionString));
                await using (successfulPoster.Context)
                {
                    var store = new AccountingFinancialYearCloseRunCommandStore(
                        successfulPoster.Context,
                        audit.Object,
                        successfulPoster);
                    await store.ReopenAsync(
                        1,
                        200,
                        " correction ",
                        postingDate,
                        14,
                        CancellationToken.None);
                }

                Assert.Equal(1, Volatile.Read(ref reopenAuditCount));
                await using var final = CreateContext(database.ConnectionString);
                var reopened = await final.AccountingFinancialYearCloseRuns
                    .SingleAsync(value => value.Id == 200);
                Assert.Equal(AccountingFinancialYearCloseRunStatuses.Reopened, reopened.Status);
                Assert.Equal("correction", reopened.ReopenReason);
                Assert.NotNull(reopened.ReversalJournalEntryId);
                Assert.False((await final.FinancialYears
                    .SingleAsync(value => value.Id == 1)).IsClosed);
                Assert.Equal(
                    JournalEntryStatuses.Reversed,
                    (await final.JournalEntries
                        .SingleAsync(value => value.Id == 900)).Status);
                Assert.Equal(
                    AccountingJournalTypes.JournalReversal,
                    (await final.JournalEntries.SingleAsync(
                        value => value.Id == reopened.ReversalJournalEntryId)).JournalType);
                Assert.Equal(
                    900,
                    (await final.JournalEntries.SingleAsync(
                        value => value.Id == reopened.ReversalJournalEntryId))
                    .ReversalOfJournalEntryId);
            });
    }

    private static void SeedApprovedRun(RetailPOSDbContext context)
    {
        var endDate = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc);
        context.Businesses.Add(new Business
        {
            Id = 1,
            Name = "Financial year execution",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        context.FinancialYears.Add(new FinancialYear
        {
            Id = 1,
            BusinessId = 1,
            YearCode = "FY26",
            Name = "FY26",
            StartDate = UtcDate(2026, 1, 1),
            EndDate = endDate,
            IsCurrent = true
        });
        context.FinancialYears.Add(new FinancialYear
        {
            Id = 2,
            BusinessId = 1,
            YearCode = "FY27",
            Name = "FY27",
            StartDate = UtcDate(2027, 1, 1),
            EndDate = UtcDate(2027, 12, 31)
        });
        context.AccountingPeriods.Add(new AccountingPeriod
        {
            Id = 10,
            FinancialYearId = 1,
            PeriodNumber = 12,
            Name = "December",
            StartDate = UtcDate(2026, 12, 1),
            EndDate = endDate,
            Status = AccountingPeriodStatuses.Locked
        });
        context.AccountingPeriods.Add(new AccountingPeriod
        {
            Id = 11,
            FinancialYearId = 2,
            PeriodNumber = 1,
            Name = "January",
            StartDate = UtcDate(2027, 1, 1),
            EndDate = UtcDate(2027, 1, 31),
            Status = AccountingPeriodStatuses.Open
        });
        context.AccountingPeriodCloseRuns.Add(new AccountingPeriodCloseRun
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
        context.AccountingFinancialYearCloseRuns.Add(
            new AccountingFinancialYearCloseRun
            {
                Id = 200,
                BusinessId = 1,
                FinancialYearId = 1,
                FinalAccountingPeriodId = 10,
                FinalPeriodCloseRunId = 100,
                RunNumber = 1,
                Status = AccountingFinancialYearCloseRunStatuses.Approved,
                PreparedBy = 1,
                CreatedBy = 1
            });
        context.ChartAccounts.AddRange(
            new ChartAccount
            {
                Id = 3,
                BusinessId = 1,
                AccountCode = "3200",
                AccountName = "Retained",
                AccountType = ChartAccountTypes.Equity,
                NormalBalanceType = NormalBalanceTypes.Credit,
                IsActive = true
            },
            new ChartAccount
            {
                Id = 4,
                BusinessId = 1,
                AccountCode = "3000",
                AccountName = "Capital",
                AccountType = ChartAccountTypes.Equity,
                NormalBalanceType = NormalBalanceTypes.Credit,
                IsActive = true
            },
            new ChartAccount
            {
                Id = 5,
                BusinessId = 1,
                AccountCode = "3100",
                AccountName = "Drawings",
                AccountType = ChartAccountTypes.Equity,
                NormalBalanceType = NormalBalanceTypes.Debit,
                IsActive = true
            },
            new ChartAccount
            {
                Id = 20,
                BusinessId = 1,
                AccountCode = "4000",
                AccountName = "Sales",
                AccountType = ChartAccountTypes.Income,
                NormalBalanceType = NormalBalanceTypes.Credit,
                IsActive = true
            },
            new ChartAccount
            {
                Id = 21,
                BusinessId = 1,
                AccountCode = "5000",
                AccountName = "COGS",
                AccountType = ChartAccountTypes.CostOfGoodsSold,
                NormalBalanceType = NormalBalanceTypes.Debit,
                IsActive = true
            });
        context.AccountingConfigurations.Add(new AccountingConfiguration
        {
            BusinessId = 1,
            RetainedEarningsAccountId = 3,
            OwnerCapitalAccountId = 4,
            OwnerDrawingsAccountId = 5
        });
        context.JournalEntries.AddRange(
            new JournalEntry
            {
                Id = 1000,
                BusinessId = 1,
                FinancialYearId = 1,
                AccountingPeriodId = 10,
                JournalType = AccountingJournalTypes.SystemJournal,
                ReferenceType = "sale",
                ReferenceId = "1",
                TransactionDate = endDate,
                PostingDate = endDate,
                Status = JournalEntryStatuses.Posted,
                Lines =
                [
                    new JournalEntryLine { AccountId = 3, DebitAmount = 100m },
                    new JournalEntryLine { AccountId = 20, CreditAmount = 100m }
                ]
            },
            new JournalEntry
            {
                Id = 1001,
                BusinessId = 1,
                FinancialYearId = 1,
                AccountingPeriodId = 10,
                JournalType = AccountingJournalTypes.SystemJournal,
                ReferenceType = "cogs",
                ReferenceId = "2",
                TransactionDate = endDate,
                PostingDate = endDate,
                Status = JournalEntryStatuses.Posted,
                Lines =
                [
                    new JournalEntryLine { AccountId = 21, DebitAmount = 40m },
                    new JournalEntryLine { AccountId = 5, DebitAmount = 20m },
                    new JournalEntryLine { AccountId = 3, CreditAmount = 60m }
                ]
            });
    }

    private static RetailPOSDbContext CreateContext(string connectionString) =>
        new(new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseNpgsql(connectionString)
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

    private sealed class TestFinancialYearCloseJournalPosting(
        RetailPOSDbContext context) : IAccountingFinancialYearCloseJournalPosting
    {
        public RetailPOSDbContext Context { get; } = context;
        public List<FinancialYearCloseJournalDraft> Drafts { get; } = [];

        private readonly ManualJournalDraftStore _journalStore = new(
            context,
            Mock.Of<IAuditService>(),
            new TestJournalNumberAllocator(),
            NullLogger<ManualJournalDraftStore>.Instance);

        public async Task<long> CreateOrGetSystemDraftAsync(
            long businessId,
            FinancialYearCloseJournalDraft draft,
            long actorUserId,
            string idempotencyDiscriminator,
            CancellationToken cancellationToken)
        {
            Drafts.Add(draft);
            return await _journalStore.CreateOrGetSystemDraftAsync(
                businessId,
                actorUserId,
                new ManualJournalDraftInput(
                    draft.JournalType,
                    draft.TransactionDate,
                    draft.PostingDate,
                    draft.BusinessDate,
                    draft.ReferenceType,
                    draft.ReferenceId,
                    null,
                    draft.Description,
                    null,
                    null,
                    null,
                    null,
                    draft.Lines.Select(line => new ManualJournalDraftLineInput(
                        line.AccountId,
                        line.Description,
                        line.DebitAmount,
                        line.CreditAmount,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null)).ToList()),
                idempotencyDiscriminator,
                cancellationToken);
        }

        public Task<long> PostApprovedYearEndClosingAsync(
            long businessId,
            long journalEntryId,
            long financialYearCloseRunId,
            long actorUserId,
            CancellationToken cancellationToken) =>
            _journalStore.PostApprovedYearEndClosingAsync(
                businessId,
                actorUserId,
                journalEntryId,
                financialYearCloseRunId,
                cancellationToken);

        public Task<long> ReverseYearEndJournalAsync(
            long businessId,
            long journalEntryId,
            string reason,
            DateTime postingDate,
            long actorUserId,
            CancellationToken cancellationToken) =>
            _journalStore.ReverseAsync(
                businessId,
                actorUserId,
                journalEntryId,
                new ManualJournalReversalInput(
                    reason,
                    postingDate.Date,
                    postingDate.Date),
                cancellationToken);
    }

    private sealed class TestJournalNumberAllocator : IJournalNumberAllocator
    {
        private int _nextNumber;

        public Task<string> AllocateNextJournalNumberAsync(
            long businessId,
            long? financialYearId,
            DateTime postingDate,
            long? actorUserId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult($"TEST-{Interlocked.Increment(ref _nextNumber)}");
    }
}
