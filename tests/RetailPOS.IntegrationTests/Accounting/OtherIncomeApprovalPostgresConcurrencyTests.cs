using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RetailPOS.API.Services;
using RetailPOS.Application.AccountingJournals.Abstractions;
using RetailPOS.Application.OtherIncome.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.AccountingJournals;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.OtherIncome;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Accounting;

public sealed class OtherIncomeApprovalPostgresConcurrencyTests
{
    static OtherIncomeApprovalPostgresConcurrencyTests() =>
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

    [Fact]
    public async Task ConcurrentApproval_AfterBothPreflightReadsPostsAndAuditsOnce()
    {
        await PostgresTestDatabase.RunAsync(
            ResolveBaseConnectionString(),
            PostgresTestDatabasePrefix.OtherIncomeApproval,
            PostgresTestDatabaseStrategy.FreshMigration,
            async database =>
            {
                await using (var setup = CreateContext(database.ConnectionString))
                {
                    await setup.Database.EnsureCreatedAsync();
                    Seed(setup);
                    await setup.SaveChangesAsync();
                }

                var tenant = new Mock<ITenantAccessService>();
                tenant.Setup(value => value.RequireBusinessId()).Returns(1);
                var audit = new Mock<IAuditService>();
                var approvalAuditCount = 0;
                audit.Setup(value => value.RecordAsync(
                        It.IsAny<AuditEventInput>(),
                        It.IsAny<CancellationToken>()))
                    .Callback<AuditEventInput, CancellationToken>((input, _) =>
                    {
                        if (input.Summary == "Other income approved: 1")
                            Interlocked.Increment(ref approvalAuditCount);
                    })
                    .Returns(Task.CompletedTask);

                var barrier = new OtherIncomeApprovalPreflightBarrier();

                async Task<Exception?> ApproveAsync()
                {
                    await using var context = CreateContext(database.ConnectionString);
                    var journalStore = new ManualJournalDraftStore(
                        context,
                        audit.Object,
                        new JournalNumberAllocator(context),
                        NullLogger<ManualJournalDraftStore>.Instance);
                    var store = new OtherIncomeCommandStore(
                        context,
                        audit.Object,
                        NullLogger<OtherIncomeCommandStore>.Instance,
                        new OtherIncomeApprovalJournalPoster(
                            SupplierPaymentCreationTestHarness.CreateJournalCommandSender(journalStore),
                            tenant.Object),
                        new OtherIncomeApprovalPreflightResolver(barrier));

                    try
                    {
                        await store.ApproveAsync(1, 101, 1, null, CancellationToken.None);
                        return null;
                    }
                    catch (Exception exception)
                    {
                        return exception;
                    }
                }

                var outcomes = await Task.WhenAll(ApproveAsync(), ApproveAsync());

                Assert.Single(outcomes, outcome => outcome is null);
                var duplicateFailure = Assert.Single(outcomes, outcome => outcome is not null);
                Assert.IsType<InvalidOperationException>(duplicateFailure);

                await using var verify = CreateContext(database.ConnectionString);
                var entry = await verify.OtherIncomeEntries.SingleAsync(value => value.Id == 1);
                Assert.Equal(OtherIncomeStatuses.Approved, entry.Status);
                Assert.NotNull(entry.JournalEntryId);
                Assert.Equal(101L, entry.ApprovedBy);
                var journal = await verify.JournalEntries.SingleAsync(journalEntry =>
                    journalEntry.BusinessId == 1
                    && journalEntry.IsSystemGenerated
                    && journalEntry.ReferenceType == "OtherIncome"
                    && journalEntry.ReferenceId == "1"
                    && journalEntry.Status == JournalEntryStatuses.Posted);
                Assert.Equal(entry.JournalEntryId, journal.Id);
                Assert.Equal(AccountingJournalTypes.SystemJournal, journal.JournalType);
                Assert.Equal(new DateTime(2026, 3, 15), journal.BusinessDate);
                Assert.Equal(new DateTime(2026, 3, 15), journal.PostingDate);
                Assert.Equal(101L, journal.CreatedBy);
                Assert.Equal(101L, journal.PostedBy);
                var lines = await verify.JournalEntryLines
                    .Where(line => line.JournalEntryId == journal.Id)
                    .ToDictionaryAsync(line => line.AccountId);
                Assert.Equal(3, lines.Count);
                Assert.Equal(110m, lines[101].DebitAmount);
                Assert.Equal(100m, lines[601].CreditAmount);
                Assert.Equal(10m, lines[201].CreditAmount);
                Assert.Equal(1, Volatile.Read(ref approvalAuditCount));
            });
    }

    private static RetailPOSDbContext CreateContext(string connectionString) =>
        new(new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseNpgsql(connectionString)
            .Options);

    private static void Seed(RetailPOSDbContext db)
    {
        db.Businesses.Add(new Business { Id = 1, Name = "Other income approval race" });
        db.ChartAccounts.AddRange(
            Account(101, "Bank", ChartAccountTypes.Asset, NormalBalanceTypes.Debit),
            Account(201, "Output VAT", ChartAccountTypes.Liability, NormalBalanceTypes.Credit),
            Account(601, "Other income", ChartAccountTypes.Income, NormalBalanceTypes.Credit));
        db.AccountingConfigurations.Add(new AccountingConfiguration
        {
            Id = 1,
            BusinessId = 1,
            DefaultBankAccountId = 101,
            SupplierPayableAccountId = 101,
            VatPayableAccountId = 201,
            VatReceivableAccountId = 101,
            CashAccountId = 101,
            InventoryAccountId = 101,
            SalesAccountId = 601,
            SalesDiscountAccountId = 101,
            SalesReturnAccountId = 101,
            CogsAccountId = 101,
            InventoryShortageAccountId = 101,
            InventoryGainAccountId = 601,
            CustomerAdvanceAccountId = 101,
            OtherIncomeAccountId = 601,
            OwnerCapitalAccountId = 101,
            OwnerDrawingsAccountId = 101,
            RetainedEarningsAccountId = 101,
            RoundingDifferenceAccountId = 101,
            CashDifferenceGainAccountId = 601,
            CashDifferenceLossAccountId = 101,
            JournalNumberPrefix = "OI"
        });
        db.FinancialYears.Add(new FinancialYear
        {
            Id = 1,
            BusinessId = 1,
            YearCode = "FY26",
            Name = "2026",
            StartDate = new DateTime(2026, 1, 1),
            EndDate = new DateTime(2026, 12, 31),
            IsCurrent = true
        });
        db.AccountingPeriods.Add(new AccountingPeriod
        {
            Id = 1,
            FinancialYearId = 1,
            PeriodNumber = 3,
            Name = "March",
            StartDate = new DateTime(2026, 3, 1),
            EndDate = new DateTime(2026, 3, 31),
            Status = AccountingPeriodStatuses.Open
        });
        db.OtherIncomeEntries.Add(new OtherIncomeEntry
        {
            Id = 1,
            BusinessId = 1,
            IncomeDate = new DateTime(2026, 3, 15),
            IncomeCategory = "Rebate",
            Amount = 110m,
            VatRate = 10m,
            VatAmount = 10m,
            TaxableAmount = 100m,
            PaymentAccountId = 101,
            Status = OtherIncomeStatuses.Submitted,
            SubmittedAt = new DateTime(2026, 3, 15),
            SubmittedBy = 101,
            CreatedAt = new DateTime(2026, 3, 15),
            CreatedBy = 101,
            UpdatedAt = new DateTime(2026, 3, 15),
            UpdatedBy = 101
        });
    }

    private static ChartAccount Account(long id, string name, string type, string normalBalance) =>
        new()
        {
            Id = id,
            BusinessId = 1,
            AccountCode = id.ToString(),
            AccountName = name,
            AccountType = type,
            NormalBalanceType = normalBalance,
            IsActive = true
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

    private sealed class OtherIncomeApprovalPreflightBarrier
    {
        private readonly TaskCompletionSource _bothArrived =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;

        public async Task WaitAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _arrivals) == 2)
                _bothArrived.TrySetResult();
            await _bothArrived.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class OtherIncomeApprovalPreflightResolver(
        OtherIncomeApprovalPreflightBarrier barrier)
        : IOtherIncomePostingAccountResolver
    {
        public async Task<long> ResolveAccountIdAsync(
            long businessId,
            string accountSlot,
            long? outletId,
            CancellationToken cancellationToken)
        {
            if (accountSlot == "other_income")
                await barrier.WaitAsync(cancellationToken);
            return accountSlot switch
            {
                "other_income" => 601,
                "vat_payable" => 201,
                _ => throw new InvalidOperationException($"Unexpected posting slot '{accountSlot}'.")
            };
        }
    }
}
