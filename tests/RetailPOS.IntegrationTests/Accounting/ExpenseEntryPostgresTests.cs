using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using RetailPOS.Application.Expenses.Abstractions;
using RetailPOS.Application.Settings.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Expenses;
using RetailPOS.IntegrationTests.Infrastructure;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Accounting;

public sealed class ExpenseEntryPostgresTests
{
    private static readonly DateTime Noon = new(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);

    static ExpenseEntryPostgresTests() => AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

    [Fact]
    public async Task ConcurrentCreates_IssueUniqueGaplessReferences() => await RunAsync(async database =>
    {
        const int creators = 12;
        var numbers = await Task.WhenAll(Enumerable.Range(0, creators).Select(async _ =>
        {
            await using var context = Context(database);
            var id = await Store(context, Audit().Object, Noon).CreateAsync(1, 1, Input(outletId: 11), CancellationToken.None);
            return await context.Expenses.AsNoTracking().Where(expense => expense.Id == id).Select(expense => expense.ExpenseNumber).SingleAsync();
        }));

        Assert.Equal(
            Enumerable.Range(1, creators).Select(sequence => $"20261105-DHK01-{sequence:D4}"),
            numbers.Order());
    });

    [Fact]
    public async Task FailedCreate_RollsBackTheExpenseAndReturnsTheNumber() => await RunAsync(async database =>
    {
        var failingAudit = new Mock<IAuditService>();
        failingAudit
            .Setup(audit => audit.RecordAsync(It.IsAny<AuditEventInput>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("audit down"));

        await using (var context = Context(database))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                Store(context, failingAudit.Object, Noon).CreateAsync(1, 1, Input(outletId: 11), CancellationToken.None));
        }

        await using var verify = Context(database);
        Assert.Equal(0, await verify.Expenses.CountAsync());

        var id = await Store(verify, Audit().Object, Noon).CreateAsync(1, 1, Input(outletId: 11), CancellationToken.None);
        Assert.Equal("20261105-DHK01-0001", (await verify.Expenses.AsNoTracking().SingleAsync(expense => expense.Id == id)).ExpenseNumber);
    });

    [Fact]
    public async Task Sequences_AreSeparatePerBusinessAndDay_AndCoexistWithJournalSequences() => await RunAsync(async database =>
    {
        await using var context = Context(database);
        context.AccountingNumberSequences.Add(new AccountingNumberSequence
        {
            BusinessId = 1, SequenceType = AccountingSequenceTypes.Journal, ScopeKey = "continuous", LastIssuedNumber = 500
        });
        await context.SaveChangesAsync();

        Assert.Equal("20261105-BIZ-0001", await AllocateAsync(context, 1, Noon));
        Assert.Equal("20261105-BIZ-0002", await AllocateAsync(context, 1, Noon));
        Assert.Equal("20261105-BIZ-0001", await AllocateAsync(context, 2, Noon));
        Assert.Equal("20261106-BIZ-0001", await AllocateAsync(context, 1, Noon.AddDays(1)));

        var journal = await context.AccountingNumberSequences.AsNoTracking().SingleAsync(row => row.SequenceType == AccountingSequenceTypes.Journal);
        Assert.Equal(500, journal.LastIssuedNumber);
    });

    [Fact]
    public async Task ReferenceDate_UsesBusinessZone_WithOutletOverride() => await RunAsync(async database =>
    {
        // 20:00 UTC is already the next day in Dhaka (+06:00), the Business default.
        var evening = new DateTime(2026, 11, 5, 20, 0, 0, DateTimeKind.Utc);
        await using var context = Context(database);

        Assert.StartsWith("20261106-", await AllocateAsync(context, 1, evening));
        Assert.StartsWith("20261106-WH21-", await AllocateAsync(context, 1, evening, warehouseId: 21));
        Assert.StartsWith("20261105-UTCO-", await AllocateAsync(context, 1, evening, outletId: 12));
    });

    [Fact]
    public async Task Database_RefusesAnExpenseWithBothOutletAndWarehouse() => await RunAsync(async database =>
    {
        await using var context = Context(database);
        context.Expenses.Add(new Expense
        {
            BusinessId = 1, Category = "Utilities", Amount = 1m, ExpenseDate = Noon.Date, OutletId = 11, WarehouseId = 21, ExpenseNumber = "BOTH"
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    });

    [Fact]
    public async Task ParallelUploads_CannotExceedTheCountLimit_AndLeaveNoOrphanFiles() => await RunAsync(async database =>
    {
        var root = Path.Combine(Path.GetTempPath(), $"RetailPosExpenseFiles_{Guid.NewGuid():N}");
        try
        {
            long expenseId;
            await using (var seed = Context(database))
                expenseId = await Store(seed, Audit().Object, Noon).CreateAsync(1, 1, Input(outletId: 11), CancellationToken.None);

            var options = Options.Create(new ExpenseAttachmentOptions { StorageRoot = root, MaxFilesPerExpense = 3, MaxFileSizeBytes = 1024 });
            var outcomes = await Task.WhenAll(Enumerable.Range(0, 8).Select(async index =>
            {
                await using var context = Context(database);
                var store = new ExpenseAttachmentStore(
                    context,
                    new ExpenseAttachmentFileStore(options),
                    new AllowAllExpenseLocations(),
                    Audit().Object,
                    options,
                    NullLogger<ExpenseAttachmentStore>.Instance);
                using var content = new MemoryStream("%PDF-1.7 parallel test"u8.ToArray());
                try
                {
                    await store.UploadAsync(1, 1, expenseId, content, $"{index}.pdf", CancellationToken.None);
                    return true;
                }
                catch (InvalidOperationException)
                {
                    return false;
                }
            }));

            await using var verify = Context(database);
            Assert.Equal(3, outcomes.Count(accepted => accepted));
            Assert.Equal(3, await verify.ExpenseAttachments.CountAsync());
            Assert.Equal(3, Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    });

    [Fact]
    public async Task Backfill_FlagsOnlyConfiguredAndBankLinkedActiveFundingAccounts() => await RunAsync(async database =>
    {
        await using var context = Context(database);
        ChartAccount NewAccount(long id, string type, bool active = true) => new()
        {
            Id = id, BusinessId = 1, AccountCode = $"A{id}", AccountName = $"A{id}", AccountType = type,
            NormalBalanceType = NormalBalanceTypes.Debit, IsActive = active
        };
        context.ChartAccounts.AddRange(
            NewAccount(1000, ChartAccountTypes.Asset),    // business cash
            NewAccount(1010, ChartAccountTypes.Asset),    // business petty cash
            NewAccount(1020, ChartAccountTypes.Asset),    // business bank
            NewAccount(1030, ChartAccountTypes.Asset),    // outlet cash
            NewAccount(1040, ChartAccountTypes.Asset),    // bank register
            NewAccount(1050, ChartAccountTypes.Asset, active: false), // inactive bank register
            NewAccount(1500, ChartAccountTypes.Asset),    // inventory: never funding
            NewAccount(2000, ChartAccountTypes.Liability), // supplier payable, also wrongly registered as a bank
            NewAccount(2100, ChartAccountTypes.Liability)); // other liability: left for accounting to decide
        await context.SaveChangesAsync();
        context.AccountingConfigurations.Add(new AccountingConfiguration
        {
            BusinessId = 1, CashAccountId = 1000, PettyCashAccountId = 1010, DefaultBankAccountId = 1020,
            InventoryAccountId = 1500, SupplierPayableAccountId = 2000, JournalNumberPrefix = "JR"
        });
        context.AccountingOutletConfigurations.Add(new AccountingOutletConfiguration { BusinessId = 1, OutletId = 11, OutletCashAccountId = 1030 });
        context.BankAccounts.AddRange(
            new BankAccount { BusinessId = 1, ChartAccountId = 1040, DisplayName = "Current" },
            new BankAccount { BusinessId = 1, ChartAccountId = 1050, DisplayName = "Closed" },
            new BankAccount { BusinessId = 1, ChartAccountId = 2000, DisplayName = "Wrong" });
        await context.SaveChangesAsync();

        await context.Database.ExecuteSqlRawAsync(
            RetailPOS.Infrastructure.Data.Migrations.ExpenseEntryEnhancements.BackfillExpensePaymentAccountsSql);

        var flagged = await context.ChartAccounts.AsNoTracking()
            .Where(account => account.IsExpensePaymentAccount && account.Id >= 1000)
            .Select(account => account.Id)
            .OrderBy(id => id)
            .ToListAsync();
        Assert.Equal([1000L, 1010L, 1020L, 1030L, 1040L], flagged);
    });

    [Fact]
    public async Task Migration_AppliesRollsBackAndReapplies() => await PostgresTestDatabase.RunAsync(
        ResolveBaseConnectionString(),
        PostgresTestDatabasePrefix.ExpenseApproval,
        PostgresTestDatabaseStrategy.FreshMigration,
        async database =>
        {
            await using var context = Context(database);
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync();

            await migrator.MigrateAsync("20261105120247_PosV2BusinessPoweredByReceiptFooter");
            var columnCount = await context.Database
                .SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM information_schema.columns WHERE table_name = 'expenses' AND column_name = 'warehouse_id'")
                .SingleAsync();
            Assert.Equal(0, columnCount);

            await migrator.MigrateAsync();
            Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        });

    private static Task RunAsync(Func<PostgresTestDatabase, Task> test) =>
        PostgresTestDatabase.RunAsync(
            ResolveBaseConnectionString(),
            PostgresTestDatabasePrefix.ExpenseApproval,
            PostgresTestDatabaseStrategy.FreshMigration,
            async database =>
            {
                await using (var setup = Context(database))
                {
                    await setup.Database.EnsureCreatedAsync();
                    setup.Businesses.AddRange(new Business { Id = 1, Name = "Expense entry" }, new Business { Id = 2, Name = "Other" });
                    setup.Outlets.AddRange(
                        new Outlet { Id = 11, BusinessId = 1, Name = "Dhaka", Address = "X", InvoicePrefix = "DHK01" },
                        new Outlet { Id = 12, BusinessId = 1, Name = "UTC outlet", Address = "X", InvoicePrefix = "UTCO", TimeZoneId = "UTC" });
                    setup.Warehouses.Add(new Warehouse { Id = 21, BusinessId = 1, Name = "Central", Address = "X" });
                    setup.ChartAccounts.AddRange(
                        new ChartAccount
                        {
                            Id = 101, BusinessId = 1, AccountCode = "1020", AccountName = "Bank", AccountType = ChartAccountTypes.Asset,
                            NormalBalanceType = NormalBalanceTypes.Debit, IsActive = true, IsExpensePaymentAccount = true
                        },
                        new ChartAccount
                        {
                            Id = 201, BusinessId = 1, AccountCode = "7000", AccountName = "Utilities", AccountType = ChartAccountTypes.Expense,
                            NormalBalanceType = NormalBalanceTypes.Debit, IsActive = true
                        });
                    setup.ExpenseCategories.Add(new ExpenseCategory { Id = 1, BusinessId = 1, Name = "Utilities", ExpenseAccountId = 201, IsActive = true });
                    setup.BankAccounts.Add(new BankAccount { BusinessId = 1, ChartAccountId = 101, DisplayName = "Current" });
                    await setup.SaveChangesAsync();
                }

                await test(database);
            });

    private static RetailPOSDbContext Context(PostgresTestDatabase database) =>
        new(new DbContextOptionsBuilder<RetailPOSDbContext>().UseNpgsql(database.ConnectionString).Options);

    private static ExpenseCommandStore Store(RetailPOSDbContext context, IAuditService audit, DateTime utcNow) => new(
        context,
        audit,
        NullLogger<ExpenseCommandStore>.Instance,
        Mock.Of<IExpenseApprovalJournalPoster>(),
        Mock.Of<IExpensePostingAccountResolver>(),
        new StubMakerCheckerPolicy(),
        new AllowAllExpenseLocations(),
        Allocator(context, utcNow));

    private static ExpenseNumberAllocator Allocator(RetailPOSDbContext context, DateTime utcNow) => new(
        context,
        Mock.Of<ICompanyTimeZoneProvider>(zones =>
            zones.GetTimeZoneIdAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()) == Task.FromResult("Asia/Dhaka")),
        new FixedClock(utcNow));

    private static async Task<string> AllocateAsync(
        RetailPOSDbContext context,
        long businessId,
        DateTime utcNow,
        long? outletId = null,
        long? warehouseId = null)
    {
        await using var transaction = await context.Database.BeginTransactionAsync();
        var number = await Allocator(context, utcNow).AllocateAsync(businessId, outletId, warehouseId, 1, CancellationToken.None);
        await transaction.CommitAsync();
        return number;
    }

    private static Mock<IAuditService> Audit()
    {
        var audit = new Mock<IAuditService>();
        audit.Setup(service => service.RecordAsync(It.IsAny<AuditEventInput>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return audit;
    }

    private static ExpenseCreationInput Input(long? outletId = null, long? warehouseId = null) => new(
        1, 10m, null, null, null, null, null, 101, null, null, null, new DateTime(2026, 3, 15), outletId, null, warehouseId);

    private static string ResolveBaseConnectionString()
    {
        var configured = Environment.GetEnvironmentVariable("RETAILPOS_TEST_POSTGRES");
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        var settingsPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "RetailPOS.API", "appsettings.json"));
        return new ConfigurationBuilder().AddJsonFile(settingsPath, optional: false).Build().GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection is not configured for PostgreSQL integration tests.");
    }

    private sealed class FixedClock(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }
}
