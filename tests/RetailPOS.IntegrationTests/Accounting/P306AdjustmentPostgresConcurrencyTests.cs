using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RetailPOS.Application.AccountingJournals.Abstractions;
using RetailPOS.Application.BankReconciliation.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.AccountingJournals;
using RetailPOS.Infrastructure.BankReconciliation;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;
using RetailPOS.API.Services;

namespace RetailPOS.Tests.Accounting;

public sealed class P306AdjustmentPostgresConcurrencyTests
{
    static P306AdjustmentPostgresConcurrencyTests() =>
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

    [Fact]
    public async Task ConcurrentAdjustmentReversalCreatesOneLinkedJournalAndOneAdjustmentTransition()
    {
        await PostgresTestDatabase.RunAsync(
            ResolveBaseConnectionString(),
            PostgresTestDatabasePrefix.StatementAdjustmentReversal,
            PostgresTestDatabaseStrategy.FreshMigration,
            async database =>
            {
                await using (var setup = CreateContext(database.ConnectionString))
                {
                    await setup.Database.EnsureCreatedAsync();
                    Seed(setup);
                    await setup.SaveChangesAsync();
                }

                var barrier = new ReversalPosterBarrier();

                async Task<Exception?> ReverseAsync(long actorUserId)
                {
                    await using var context = CreateContext(database.ConnectionString);
                    var poster = new BankStatementAdjustmentJournalPoster(
                        CreateJournalCommandSender(context),
                        Mock.Of<IAccountingConfigurationService>());
                    var store = new BankStatementAdjustmentReversalStore(
                        context,
                        new BarrierJournalPoster(poster, barrier));
                    try
                    {
                        await store.ReverseAsync(
                            1,
                            actorUserId,
                            1,
                            "duplicate request",
                            new DateTime(2026, 10, 1),
                            CancellationToken.None);
                        return null;
                    }
                    catch (Exception exception)
                    {
                        return exception;
                    }
                }

                var outcomes = await Task.WhenAll(ReverseAsync(9), ReverseAsync(10));
                Assert.Single(outcomes, outcome => outcome is null);
                Assert.IsType<InvalidOperationException>(
                    Assert.Single(outcomes, outcome => outcome is not null));

                await using var verify = CreateContext(database.ConnectionString);
                var adjustment = await verify.BankStatementAdjustments.SingleAsync();
                var reversal = await verify.JournalEntries
                    .SingleAsync(row => row.ReversalOfJournalEntryId == 200);
                var original = await verify.JournalEntries.SingleAsync(row => row.Id == 200);
                Assert.Equal(BankStatementAdjustmentStatuses.Reversed, adjustment.Status);
                Assert.Equal(reversal.Id, adjustment.ReversalJournalEntryId);
                Assert.Equal(original.Id, reversal.ReversalOfJournalEntryId);
                Assert.Equal(JournalEntryStatuses.Reversed, original.Status);
                Assert.Contains(adjustment.ReversedBy, new long?[] { 9, 10 });
                Assert.Equal(JournalEntryStatuses.Posted, reversal.Status);
            });
    }

    private static RetailPOSDbContext CreateContext(string connectionString) =>
        new(new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseNpgsql(connectionString)
            .Options);

    private static MediatR.ISender CreateJournalCommandSender(RetailPOSDbContext db)
    {
        var numbers = new Mock<IJournalNumberAllocator>();
        numbers.Setup(value => value.AllocateNextJournalNumberAsync(
                It.IsAny<long>(),
                It.IsAny<long?>(),
                It.IsAny<DateTime>(),
                It.IsAny<long?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("JR-REV");
        var audit = Mock.Of<IAuditService>(value =>
            value.RecordAsync(It.IsAny<AuditEventInput>(), It.IsAny<CancellationToken>())
            == Task.CompletedTask);
        return SupplierPaymentCreationTestHarness.CreateJournalCommandSender(
            new ManualJournalDraftStore(
                db,
                audit,
                numbers.Object,
                NullLogger<ManualJournalDraftStore>.Instance));
    }

    private static void Seed(RetailPOSDbContext db)
    {
        db.Businesses.Add(new Business { Id = 1, Name = "Adjustment reversal concurrency" });
        db.ChartAccounts.AddRange(
            Account(101, ChartAccountTypes.Asset),
            Account(601, ChartAccountTypes.Expense));
        db.BankAccounts.Add(new BankAccount
        {
            Id = 501,
            BusinessId = 1,
            ChartAccountId = 101,
            DisplayName = "Bank"
        });
        db.FinancialYears.Add(new FinancialYear
        {
            Id = 1,
            BusinessId = 1,
            YearCode = "FY26",
            Name = "FY26",
            StartDate = new DateTime(2026, 1, 1),
            EndDate = new DateTime(2026, 12, 31),
            IsCurrent = true
        });
        db.AccountingPeriods.Add(new AccountingPeriod
        {
            Id = 1,
            FinancialYearId = 1,
            PeriodNumber = 10,
            Name = "October",
            StartDate = new DateTime(2026, 10, 1),
            EndDate = new DateTime(2026, 10, 31),
            Status = AccountingPeriodStatuses.Open
        });
        db.BankStatementImports.Add(new BankStatementImport
        {
            Id = 100,
            BusinessId = 1,
            BankAccountId = 501,
            SourceFileName = "statement.csv",
            SourceFileChecksum = "adjustment-reversal",
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
            Description = "fee",
            SignedAmount = -10,
            DebitAmount = 10
        });
        db.JournalEntries.Add(new JournalEntry
        {
            Id = 200,
            BusinessId = 1,
            FinancialYearId = 1,
            JournalNumber = "JR-200",
            JournalType = "BankStatementAdjustment",
            ReferenceType = "BankStatementAdjustment",
            ReferenceId = "1",
            Status = JournalEntryStatuses.Posted,
            IsSystemGenerated = true,
            PostingDate = new DateTime(2026, 9, 10),
            TransactionDate = new DateTime(2026, 9, 10),
            BusinessDate = new DateTime(2026, 9, 10),
            Lines =
            [
                new JournalEntryLine
                {
                    Id = 2001,
                    AccountId = 601,
                    DebitAmount = 10,
                    Description = "fee"
                },
                new JournalEntryLine
                {
                    Id = 2002,
                    AccountId = 101,
                    CreditAmount = 10,
                    Description = "bank"
                }
            ]
        });
        db.BankStatementAdjustments.Add(new BankStatementAdjustment
        {
            Id = 1,
            BusinessId = 1,
            BankAccountId = 501,
            BankStatementLineId = 1001,
            AdjustmentType = BankStatementAdjustmentTypes.BankFee,
            Amount = 10,
            Status = BankStatementAdjustmentStatuses.Approved,
            JournalEntryId = 200,
            IdempotencyKey = "fee",
            ProposedBy = 9,
            CreatedBy = 9
        });
    }

    private static ChartAccount Account(long id, string type) => new()
    {
        Id = id,
        BusinessId = 1,
        AccountCode = id.ToString(),
        AccountName = $"Account {id}",
        AccountType = type,
        NormalBalanceType = type == ChartAccountTypes.Asset
            ? NormalBalanceTypes.Debit
            : NormalBalanceTypes.Credit,
        IsActive = true,
        AllowManualPosting = true
    };

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

    private sealed class ReversalPosterBarrier
    {
        private readonly TaskCompletionSource _bothReady =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;

        public Task WaitForBothAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _arrivals) == 2)
                _bothReady.TrySetResult();
            return _bothReady.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class BarrierJournalPoster(
        IBankStatementAdjustmentJournalPoster inner,
        ReversalPosterBarrier barrier)
        : IBankStatementAdjustmentJournalPoster
    {
        public Task<long> ResolvePostingAccountIdAsync(
            string adjustmentType,
            CancellationToken cancellationToken) =>
            inner.ResolvePostingAccountIdAsync(adjustmentType, cancellationToken);

        public Task<long> CreateAndPostAsync(
            BankStatementAdjustmentJournalRequest request,
            long actorUserId,
            CancellationToken cancellationToken) =>
            inner.CreateAndPostAsync(request, actorUserId, cancellationToken);

        public async Task<long> ReverseAsync(
            long businessId,
            long journalEntryId,
            string reason,
            DateTime postingDate,
            long actorUserId,
            CancellationToken cancellationToken)
        {
            await barrier.WaitForBothAsync(cancellationToken);
            return await inner.ReverseAsync(
                businessId,
                journalEntryId,
                reason,
                postingDate,
                actorUserId,
                cancellationToken);
        }
    }
}
