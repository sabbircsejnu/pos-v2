using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RetailPOS.API.Services;
using RetailPOS.Application.Expenses.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.AccountingJournals;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Expenses;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Accounting;

public sealed class ExpenseApprovalPostgresConcurrencyTests
{
    static ExpenseApprovalPostgresConcurrencyTests() =>
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

    [Fact]
    public async Task ConcurrentApproval_AfterBothPreflightReadsPostsAndAuditsOnce()
    {
        await PostgresTestDatabase.RunAsync(
            ResolveBaseConnectionString(),
            PostgresTestDatabasePrefix.ExpenseApproval,
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
                        if (input.Summary == "Expense approved: 1")
                            Interlocked.Increment(ref approvalAuditCount);
                    })
                    .Returns(Task.CompletedTask);

                var barrier = new ExpenseApprovalPreflightBarrier();

                async Task<Exception?> ApproveAsync()
                {
                    await using var context = CreateContext(database.ConnectionString);
                    var journalStore = new ManualJournalDraftStore(
                        context,
                        audit.Object,
                        new JournalNumberAllocator(context),
                        NullLogger<ManualJournalDraftStore>.Instance);
                    var store = new ExpenseCommandStore(
                        context,
                        audit.Object,
                        NullLogger<ExpenseCommandStore>.Instance,
                        new ExpenseApprovalJournalPoster(
                            SupplierPaymentCreationTestHarness.CreateJournalCommandSender(journalStore),
                            tenant.Object),
                        new ExpenseApprovalPreflightBarrierResolver(barrier),
                        new RetailPOS.IntegrationTests.Infrastructure.StubMakerCheckerPolicy(),
            new RetailPOS.IntegrationTests.Infrastructure.AllowAllExpenseLocations(),
            new RetailPOS.IntegrationTests.Infrastructure.SequentialExpenseNumbers());

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
                Assert.True(
                    duplicateFailure is InvalidOperationException,
                    duplicateFailure.ToString());
                Assert.Equal(1, Volatile.Read(ref approvalAuditCount));

                await using var verify = CreateContext(database.ConnectionString);
                var expense = await verify.Expenses.SingleAsync(entry => entry.Id == 1);
                Assert.Equal(ExpenseStatuses.Approved, expense.Status);
                Assert.NotNull(expense.JournalEntryId);
                Assert.Equal(1, await verify.JournalEntries.CountAsync(entry =>
                    entry.BusinessId == 1
                    && entry.IsSystemGenerated
                    && entry.ReferenceType == "Expense"
                    && entry.ReferenceId == "1"
                    && entry.Status == JournalEntryStatuses.Posted));
                Assert.Equal(3, await verify.JournalEntryLines.CountAsync(line =>
                    line.JournalEntryId == expense.JournalEntryId));
            });
    }

    private static RetailPOSDbContext CreateContext(string connectionString) =>
        new(new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseNpgsql(connectionString)
            .Options);

    private static void Seed(RetailPOSDbContext db)
    {
        db.Businesses.Add(new Business { Id = 1, Name = "Expense approval race" });
        db.ChartAccounts.AddRange(
            Account(101, "Bank", ChartAccountTypes.Asset, NormalBalanceTypes.Debit),
            Account(201, "Expense", ChartAccountTypes.Expense, NormalBalanceTypes.Debit),
            Account(202, "Input VAT", ChartAccountTypes.Asset, NormalBalanceTypes.Debit),
            Account(203, "Supplier Payable", ChartAccountTypes.Liability, NormalBalanceTypes.Credit));
        db.ExpenseCategories.Add(new ExpenseCategory
        {
            Id = 1,
            BusinessId = 1,
            Name = "Utilities",
            ExpenseAccountId = 201,
            IsActive = true
        });
        db.AccountingConfigurations.Add(new AccountingConfiguration
        {
            Id = 1,
            BusinessId = 1,
            DefaultBankAccountId = 101,
            SupplierPayableAccountId = 203,
            VatReceivableAccountId = 202,
            CashAccountId = 101,
            InventoryAccountId = 201,
            SalesAccountId = 201,
            SalesDiscountAccountId = 201,
            SalesReturnAccountId = 201,
            CogsAccountId = 201,
            InventoryShortageAccountId = 201,
            InventoryGainAccountId = 201,
            CustomerAdvanceAccountId = 203,
            OtherIncomeAccountId = 201,
            OwnerCapitalAccountId = 203,
            OwnerDrawingsAccountId = 203,
            RetainedEarningsAccountId = 203,
            RoundingDifferenceAccountId = 201,
            CashDifferenceGainAccountId = 201,
            CashDifferenceLossAccountId = 201,
            JournalNumberPrefix = "EXP"
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
        db.Expenses.Add(new Expense
        {
            Id = 1,
            BusinessId = 1,
            ExpenseNumber = "EXP-000001",
            ExpenseCategoryId = 1,
            Category = "Utilities",
            Amount = 110m,
            VatRate = 10m,
            VatAmount = 10m,
            TaxableAmount = 100m,
            PaymentAccountId = 101,
            ExpenseDate = new DateTime(2026, 3, 15),
            Status = ExpenseStatuses.Submitted,
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

    private sealed class ExpenseApprovalPreflightBarrier
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

    private sealed class ExpenseApprovalPreflightBarrierResolver(
        ExpenseApprovalPreflightBarrier barrier)
        : IExpensePostingAccountResolver
    {
        public async Task<long> ResolveAccountIdAsync(
            long businessId,
            string accountSlot,
            long? outletId,
            CancellationToken cancellationToken)
        {
            if (accountSlot == "supplier_payable")
                await barrier.WaitAsync(cancellationToken);
            return accountSlot switch
            {
                "supplier_payable" => 203,
                "vat_receivable" => 202,
                _ => throw new InvalidOperationException($"Unexpected posting account slot: {accountSlot}")
            };
        }
    }
}
