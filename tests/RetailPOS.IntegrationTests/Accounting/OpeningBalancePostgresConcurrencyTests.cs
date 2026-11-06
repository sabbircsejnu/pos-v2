using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RetailPOS.API.Services;
using RetailPOS.Application.OpeningBalances.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.AccountingJournals;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.OpeningBalances;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Accounting;

public sealed class OpeningBalancePostgresConcurrencyTests
{
    static OpeningBalancePostgresConcurrencyTests() =>
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

    [Fact]
    public async Task ConcurrentFirstPost_AfterBothStatusChecksCreatesOnePostedOpeningBalance()
    {
        await PostgresTestDatabase.RunAsync(
            ResolveBaseConnectionString(),
            PostgresTestDatabasePrefix.OpeningBalancePost,
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
                tenant.Setup(access => access.RequireBusinessId()).Returns(1);
                var audit = new Mock<IAuditService>();
                var postedAuditCount = 0;
                audit.Setup(service => service.RecordAsync(
                        It.IsAny<AuditEventInput>(),
                        It.IsAny<CancellationToken>()))
                    .Callback<AuditEventInput, CancellationToken>((input, _) =>
                    {
                        if (input.Summary.StartsWith("Journal posted:", StringComparison.Ordinal))
                            Interlocked.Increment(ref postedAuditCount);
                    })
                    .Returns(Task.CompletedTask);

                var preflightBarrier = new OpeningBalancePreflightBarrier();

                async Task<long> PostAsync()
                {
                    await using var context = CreateContext(database.ConnectionString);
                    var store = new OpeningBalanceCommandStore(
                        context,
                        new OpeningBalancePreflightMappingValidator(preflightBarrier),
                        new OpeningBalanceJournalPoster(
                            CreateJournalCommandSender(context, audit.Object),
                            tenant.Object));

                    return await store.PostAsync(
                        1,
                        101,
                        new OpeningBalanceInput(
                            new DateTime(2026, 1, 1),
                            "OB-2026",
                            "Opening balances",
                            [
                                new OpeningBalanceLineInput(1001, 2000m, 0m, null, null, null),
                                new OpeningBalanceLineInput(2001, 0m, 2000m, null, null, null)
                            ]),
                        CancellationToken.None);
                }

                var journalIds = await Task.WhenAll(PostAsync(), PostAsync());
                Assert.Equal(journalIds[0], journalIds[1]);

                await using var verify = CreateContext(database.ConnectionString);
                var posted = await verify.JournalEntries.SingleAsync(journal =>
                    journal.BusinessId == 1
                    && journal.IsSystemGenerated
                    && journal.JournalType == AccountingJournalTypes.OpeningBalance
                    && journal.ReferenceType == AccountingReferenceTypes.OpeningBalance
                    && journal.ReferenceId == "1"
                    && journal.Status == JournalEntryStatuses.Posted);
                Assert.Equal(journalIds[0], posted.Id);
                Assert.Equal("OB-2026", posted.ReferenceNumber);
                Assert.Equal("Opening balances", posted.Description);
                var lines = await verify.JournalEntryLines
                    .Where(line => line.JournalEntryId == posted.Id)
                    .ToDictionaryAsync(line => line.AccountId);
                Assert.Equal(2, lines.Count);
                Assert.Equal(2000m, lines[1001].DebitAmount);
                Assert.Equal(2000m, lines[2001].CreditAmount);
                Assert.Equal(1, Volatile.Read(ref postedAuditCount));
            });
    }

    [Fact]
    public async Task PostAdjustment_CreatesOneLinkedPostedJournalAndAudit()
    {
        await PostgresTestDatabase.RunAsync(
            ResolveBaseConnectionString(),
            PostgresTestDatabasePrefix.OpeningBalancePost,
            PostgresTestDatabaseStrategy.FreshMigration,
            async database =>
            {
                await using (var setup = CreateContext(database.ConnectionString))
                {
                    await setup.Database.EnsureCreatedAsync();
                    Seed(setup);
                    setup.JournalEntries.Add(new JournalEntry
                    {
                        Id = 9000,
                        BusinessId = 1,
                        JournalType = AccountingJournalTypes.OpeningBalance,
                        TransactionDate = new DateTime(2026, 1, 1),
                        PostingDate = new DateTime(2026, 1, 1),
                        BusinessDate = new DateTime(2026, 1, 1),
                        ReferenceType = AccountingReferenceTypes.OpeningBalance,
                        ReferenceId = "1",
                        JournalNumber = "OB-2026-000001",
                        Description = "Initial opening balance",
                        Status = JournalEntryStatuses.Posted,
                        IsSystemGenerated = true,
                        PostedAt = DateTime.UtcNow,
                        PostedBy = 101,
                        CreatedAt = DateTime.UtcNow,
                        CreatedBy = 101,
                        UpdatedAt = DateTime.UtcNow,
                        UpdatedBy = 101,
                        Lines =
                        [
                            new JournalEntryLine
                            {
                                AccountId = 1001,
                                DebitAmount = 2000m,
                                CreditAmount = 0m,
                                CreatedAt = DateTime.UtcNow,
                                CreatedBy = 101
                            },
                            new JournalEntryLine
                            {
                                AccountId = 2001,
                                DebitAmount = 0m,
                                CreditAmount = 2000m,
                                CreatedAt = DateTime.UtcNow,
                                CreatedBy = 101
                            }
                        ]
                    });
                    await setup.SaveChangesAsync();
                }

                var tenant = new Mock<ITenantAccessService>();
                tenant.Setup(access => access.RequireBusinessId()).Returns(1);
                var audit = new Mock<IAuditService>();
                var postedAuditCount = 0;
                audit.Setup(service => service.RecordAsync(
                        It.IsAny<AuditEventInput>(),
                        It.IsAny<CancellationToken>()))
                    .Callback<AuditEventInput, CancellationToken>((input, _) =>
                    {
                        if (input.Summary.StartsWith("Journal posted:", StringComparison.Ordinal))
                            Interlocked.Increment(ref postedAuditCount);
                    })
                    .Returns(Task.CompletedTask);

                await using (var context = CreateContext(database.ConnectionString))
                {
                    var store = new OpeningBalanceCommandStore(
                        context,
                        Mock.Of<IOpeningBalanceMappingValidator>(),
                        new OpeningBalanceJournalPoster(
                            CreateJournalCommandSender(context, audit.Object),
                            tenant.Object));
                    var adjustmentId = await store.PostAdjustmentAsync(
                        1,
                        101,
                        9000,
                        new OpeningBalanceAdjustmentInput(
                            "Correct payable carry-forward",
                            new DateTime(2026, 1, 15),
                            new DateTime(2026, 1, 15),
                            "OB-ADJ-01",
                            "Payable correction",
                            [
                                new OpeningBalanceLineInput(1001, 100m, 0m, null, null, null),
                                new OpeningBalanceLineInput(2001, 0m, 100m, null, null, null)
                            ]),
                        CancellationToken.None);

                    await using var verify = CreateContext(database.ConnectionString);
                    var adjustment = await verify.JournalEntries
                        .Include(journal => journal.Lines)
                        .SingleAsync(journal => journal.Id == adjustmentId);
                    Assert.Equal(1, adjustment.BusinessId);
                    Assert.Equal(AccountingJournalTypes.OpeningBalanceAdjustment, adjustment.JournalType);
                    Assert.Equal(AccountingReferenceTypes.OpeningBalanceAdjustment, adjustment.ReferenceType);
                    Assert.StartsWith("9000:", adjustment.ReferenceId, StringComparison.Ordinal);
                    Assert.Equal("OB-ADJ-01", adjustment.ReferenceNumber);
                    Assert.Equal(
                        "Payable correction (Reason: Correct payable carry-forward)",
                        adjustment.Description);
                    Assert.Equal(JournalEntryStatuses.Posted, adjustment.Status);
                    Assert.Equal(2, adjustment.Lines.Count);
                    Assert.Equal(1, Volatile.Read(ref postedAuditCount));
                }
            });
    }

    private static MediatR.ISender CreateJournalCommandSender(
        RetailPOSDbContext context,
        IAuditService audit)
    {
        var journalStore = new ManualJournalDraftStore(
            context,
            audit,
            new JournalNumberAllocator(context),
            NullLogger<ManualJournalDraftStore>.Instance);
        return SupplierPaymentCreationTestHarness.CreateJournalCommandSender(
            journalStore,
            new JournalEntryReadStore(context));
    }

    private static RetailPOSDbContext CreateContext(string connectionString) =>
        new(new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseNpgsql(connectionString)
            .Options);

    private static void Seed(RetailPOSDbContext db)
    {
        db.Businesses.Add(new Business { Id = 1, Name = "Opening balance concurrency" });
        db.ChartAccounts.AddRange(
            Account(1001, "Cash", ChartAccountTypes.Asset, NormalBalanceTypes.Debit),
            Account(2001, "Supplier payable", ChartAccountTypes.Liability, NormalBalanceTypes.Credit));
        db.AccountingConfigurations.Add(new AccountingConfiguration
        {
            Id = 1,
            BusinessId = 1,
            DefaultBankAccountId = 1001,
            SupplierPayableAccountId = 2001,
            VatReceivableAccountId = 1001,
            CashAccountId = 1001,
            InventoryAccountId = 1001,
            SalesAccountId = 1001,
            SalesDiscountAccountId = 1001,
            SalesReturnAccountId = 1001,
            CogsAccountId = 1001,
            InventoryShortageAccountId = 1001,
            InventoryGainAccountId = 1001,
            CustomerAdvanceAccountId = 2001,
            OtherIncomeAccountId = 1001,
            OwnerCapitalAccountId = 2001,
            OwnerDrawingsAccountId = 1001,
            RetainedEarningsAccountId = 2001,
            RoundingDifferenceAccountId = 1001,
            CashDifferenceGainAccountId = 1001,
            CashDifferenceLossAccountId = 1001,
            JournalNumberPrefix = "OB",
            JournalNumberPadding = 6,
            JournalSequenceResetPolicy = AccountingSequenceResetPolicies.PerFinancialYear
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
            PeriodNumber = 1,
            Name = "January",
            StartDate = new DateTime(2026, 1, 1),
            EndDate = new DateTime(2026, 1, 31),
            Status = AccountingPeriodStatuses.Open
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
            IsSystemAccount = true,
            AllowManualPosting = true,
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

    private sealed class OpeningBalancePreflightBarrier
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

    private sealed class OpeningBalancePreflightMappingValidator(
        OpeningBalancePreflightBarrier barrier)
        : IOpeningBalanceMappingValidator
    {
        public async Task<IReadOnlyList<string>> ValidateBalanceSheetMappingsAsync(
            long businessId,
            CancellationToken cancellationToken)
        {
            await barrier.WaitAsync(cancellationToken);
            return Array.Empty<string>();
        }
    }
}
