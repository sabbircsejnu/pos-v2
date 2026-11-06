using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RetailPOS.Application.ChartAccounts.Commands;
using RetailPOS.Core.Accounting;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.ChartAccounts;
using RetailPOS.Infrastructure.Data;
using RetailPOS.IntegrationTests.Infrastructure;

namespace RetailPOS.Tests.Accounting;

/// <summary>D5: every Active-to-Inactive path shares one dependency policy, on real PostgreSQL.</summary>
public sealed class ChartAccountDeactivationPostgresTests
{
    private const long Account = 7001;
    private const long Child = 7002;
    private const long ForeignAccount = 9001;

    static ChartAccountDeactivationPostgresTests() => AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

    public static TheoryData<bool> BothPaths => new() { true, false };

    [Theory]
    [MemberData(nameof(BothPaths))]
    public async Task ActiveExpenseCategories_BlockDeactivation_WithACount(bool viaUpdate) => await RunAsync(async database =>
    {
        await using var context = Context(database);
        context.ExpenseCategories.AddRange(
            new ExpenseCategory { BusinessId = 1, Name = "Internet", ExpenseAccountId = Account, IsActive = true },
            new ExpenseCategory { BusinessId = 1, Name = "Phone", ExpenseAccountId = Account, IsActive = true },
            new ExpenseCategory { BusinessId = 1, Name = "Old", ExpenseAccountId = Account, IsActive = false });
        await context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => DeactivateAsync(context, Account, viaUpdate));

        Assert.Equal("Cannot deactivate this account because it is used by 2 active expense categories.", error.Message);
        await AssertActiveAsync(database, Account);
    });

    [Theory]
    [MemberData(nameof(BothPaths))]
    public async Task JournalHistory_BlocksDeactivation_AndLeavesTheLedgerUntouched(bool viaUpdate) => await RunAsync(async database =>
    {
        await using var context = Context(database);
        var journal = new JournalEntry
        {
            BusinessId = 1, JournalType = "ManualJournal", TransactionDate = DateTime.UtcNow, PostingDate = DateTime.UtcNow,
            BusinessDate = DateTime.UtcNow.Date, ReferenceType = "Manual", ReferenceId = "D5-1", Status = JournalEntryStatuses.Posted,
            JournalNumber = "JR-D5-1",
            Lines =
            [
                new JournalEntryLine { AccountId = Account, DebitAmount = 25, CreditAmount = 0 },
                new JournalEntryLine { AccountId = Child, DebitAmount = 0, CreditAmount = 25 }
            ]
        };
        context.JournalEntries.Add(journal);
        await context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => DeactivateAsync(context, Account, viaUpdate));

        Assert.Contains("journal", error.Message, StringComparison.OrdinalIgnoreCase);
        await AssertActiveAsync(database, Account);
        await using var verify = Context(database);
        Assert.Equal(2, await verify.JournalEntryLines.CountAsync(line => line.JournalEntryId == journal.Id));
    });

    [Theory]
    [MemberData(nameof(BothPaths))]
    public async Task ActiveChildAccounts_BlockDeactivation(bool viaUpdate) => await RunAsync(async database =>
    {
        await using var context = Context(database);
        var child = await context.ChartAccounts.SingleAsync(account => account.Id == Child);
        child.ParentAccountId = Account;
        await context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => DeactivateAsync(context, Account, viaUpdate));

        Assert.Contains("1 active child account", error.Message);
        await AssertActiveAsync(database, Account);
    });

    [Theory]
    [MemberData(nameof(BothPaths))]
    public async Task ConfigurationSlots_NotPreviouslyChecked_BlockDeactivation(bool viaUpdate) => await RunAsync(async database =>
    {
        await using var context = Context(database);
        context.AccountingConfigurations.Add(new AccountingConfiguration
        {
            BusinessId = 1, BankFeeExpenseAccountId = Account, JournalNumberPrefix = "JR"
        });
        await context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => DeactivateAsync(context, Account, viaUpdate));

        Assert.Contains("accounting configuration", error.Message);
        await AssertActiveAsync(database, Account);
    });

    [Theory]
    [MemberData(nameof(BothPaths))]
    public async Task ActiveBankRegister_BlocksDeactivation(bool viaUpdate) => await RunAsync(async database =>
    {
        await using var context = Context(database);
        context.BankAccounts.Add(new BankAccount { BusinessId = 1, ChartAccountId = Account, DisplayName = "Current" });
        await context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => DeactivateAsync(context, Account, viaUpdate));

        Assert.Contains("1 active bank account", error.Message);
    });

    [Theory]
    [InlineData(ExpenseStatuses.Draft)]
    [InlineData(ExpenseStatuses.Submitted)]
    [InlineData(ExpenseStatuses.Rejected)]
    public async Task PendingExpenses_BlockDeactivation_ThroughEveryPendingLink(string status) => await RunAsync(async database =>
    {
        await using var context = Context(database);
        var inactiveCategory = new ExpenseCategory { BusinessId = 1, Name = "Retired", ExpenseAccountId = Account, IsActive = false };
        context.ExpenseCategories.Add(inactiveCategory);
        await context.SaveChangesAsync();
        context.Expenses.Add(new Expense
        {
            BusinessId = 1, Category = "Retired", ExpenseCategoryId = inactiveCategory.Id, Amount = 10m,
            ExpenseDate = new DateTime(2026, 3, 1), Status = status, ExpenseNumber = $"D5-{status}"
        });
        await context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => DeactivateAsync(context, Account, viaUpdate: true));

        Assert.Contains("1 unapproved expense", error.Message);
    });

    [Fact]
    public async Task ApprovedOrCancelledExpenses_DoNotBlockDeactivation() => await RunAsync(async database =>
    {
        await using var context = Context(database);
        context.Expenses.AddRange(
            new Expense
            {
                BusinessId = 1, Category = "X", ExpenseAccountId = Account, Amount = 10m,
                ExpenseDate = new DateTime(2026, 3, 1), Status = ExpenseStatuses.Approved, ExpenseNumber = "D5-A"
            },
            new Expense
            {
                BusinessId = 1, Category = "X", PaymentAccountId = Account, Amount = 10m,
                ExpenseDate = new DateTime(2026, 3, 1), Status = ExpenseStatuses.Cancelled, ExpenseNumber = "D5-C"
            });
        await context.SaveChangesAsync();

        Assert.True(await Store(context).DeactivateAsync(1, 5, Account, CancellationToken.None));
    });

    [Fact]
    public async Task EveryBlockingDependency_IsNamedTogether() => await RunAsync(async database =>
    {
        await using var context = Context(database);
        var child = await context.ChartAccounts.SingleAsync(account => account.Id == Child);
        child.ParentAccountId = Account;
        context.ExpenseCategories.Add(new ExpenseCategory { BusinessId = 1, Name = "Internet", ExpenseAccountId = Account, IsActive = true });
        await context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => DeactivateAsync(context, Account, viaUpdate: false));

        Assert.Contains("1 active expense category", error.Message);
        Assert.Contains("1 active child account", error.Message);
    });

    [Fact]
    public async Task ForeignBusinessDependencies_AreNeitherCountedNorReachable() => await RunAsync(async database =>
    {
        await using var context = Context(database);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Store(context).DeactivateAsync(1, 5, ForeignAccount, CancellationToken.None));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => DeactivateAsync(context, ForeignAccount, viaUpdate: true));

        Assert.True(await Store(context).DeactivateAsync(1, 5, Account, CancellationToken.None));
    });

    [Fact]
    public async Task Update_OfAnInactiveAccount_NeedsNoDependencyCheck_AndReactivates() => await RunAsync(async database =>
    {
        await using var context = Context(database);
        Assert.True(await Store(context).DeactivateAsync(1, 5, Account, CancellationToken.None));
        context.ExpenseCategories.Add(new ExpenseCategory { BusinessId = 1, Name = "Later", ExpenseAccountId = Account, IsActive = false });
        await context.SaveChangesAsync();

        var renamed = await Store(context).UpdateAsync(1, 5, Command(Account, "Renamed", isActive: false), CancellationToken.None);
        Assert.False(renamed.IsActive);

        var reactivated = await Store(context).UpdateAsync(1, 5, Command(Account, "Renamed", isActive: true), CancellationToken.None);
        Assert.True(reactivated.IsActive);
        Assert.Equal("7001", reactivated.AccountCode);
    });

    [Fact]
    public async Task Reactivation_UnderAnInactiveParent_IsRefused() => await RunAsync(async database =>
    {
        await using var context = Context(database);
        var parent = await context.ChartAccounts.SingleAsync(account => account.Id == Child);
        var account = await context.ChartAccounts.SingleAsync(row => row.Id == Account);
        account.ParentAccountId = Child;
        account.IsActive = false;
        parent.IsActive = false;
        await context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Store(context).UpdateAsync(1, 5, Command(Account, "Back", isActive: true, parentId: Child), CancellationToken.None));

        Assert.Contains("Parent account must be active", error.Message);
    });

    private static Task DeactivateAsync(RetailPOSDbContext context, long accountId, bool viaUpdate) => viaUpdate
        ? Store(context).UpdateAsync(1, 5, Command(accountId, "Renamed", isActive: false), CancellationToken.None)
        : Store(context).DeactivateAsync(1, 5, accountId, CancellationToken.None);

    private static UpdateChartAccountCommand Command(long id, string name, bool isActive, long? parentId = null) =>
        new(id, name, parentId, false, true, isActive, 0, 5);

    private static async Task AssertActiveAsync(PostgresTestDatabase database, long accountId)
    {
        await using var verify = Context(database);
        Assert.True((await verify.ChartAccounts.AsNoTracking().SingleAsync(account => account.Id == accountId)).IsActive);
    }

    private static ChartAccountCommandStore Store(RetailPOSDbContext context) =>
        new(context, null, NullLogger<ChartAccountCommandStore>.Instance);

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
                    setup.Businesses.AddRange(new Business { Id = 1, Name = "Own" }, new Business { Id = 2, Name = "Other" });
                    setup.ChartAccounts.AddRange(
                        NewAccount(Account, 1, "7001"),
                        NewAccount(Child, 1, "7002"),
                        NewAccount(ForeignAccount, 2, "7001"));
                    await setup.SaveChangesAsync();
                }

                await test(database);
            });

    private static ChartAccount NewAccount(long id, long businessId, string code) => new()
    {
        Id = id, BusinessId = businessId, AccountCode = code, AccountName = $"Account {code}", AccountType = ChartAccountTypes.Expense,
        NormalBalanceType = NormalBalanceTypes.Debit, IsActive = true
    };

    private static RetailPOSDbContext Context(PostgresTestDatabase database) =>
        new(new DbContextOptionsBuilder<RetailPOSDbContext>().UseNpgsql(database.ConnectionString).Options);

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
}
