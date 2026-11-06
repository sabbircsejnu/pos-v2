using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RetailPOS.API.Services;
using RetailPOS.Application.ChartAccounts.Commands;
using RetailPOS.Application.Expenses.Abstractions;
using RetailPOS.Core.Accounting;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.AccountingJournals;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.ChartAccounts;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Expenses;
using RetailPOS.IntegrationTests.Infrastructure;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Accounting;

/// <summary>
/// Chart of Accounts, category mapping and expense approval together, through the real journal engine on PostgreSQL:
/// the category decides the debit, the payment account the credit, and nothing else changes.
/// </summary>
public sealed class ExpenseAccountingLifecyclePostgresTests
{
    private const long Business = 1;
    private const long Outlet = 11;
    private const long CashAccount = 1000;
    private const long PettyAccount = 1010;
    private const long BankAccountId = 1020;
    private const long VatAccount = 1300;
    private const long SupplierPayable = 2000;
    private const long SalesDiscount = 4010;
    private const long ForeignExpense = 9700;
    private static readonly DateTime Date = new(2026, 3, 15);

    static ExpenseAccountingLifecyclePostgresTests() => AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

    [Fact]
    public async Task CashExpense_FromANewAccountAndCategory_DebitsTheCategoryAccountAndCreditsCash() => await RunAsync(async database =>
    {
        await using var context = Context(database);
        var account = await new ChartAccountCommandStore(context, null, NullLogger<ChartAccountCommandStore>.Instance).CreateAsync(
            Business, 5,
            new CreateChartAccountCommand("7400", "Internet & Communication Expense", ChartAccountTypes.Expense, NormalBalanceTypes.Debit, null, false, true, true, 0, 5),
            CancellationToken.None);
        var store = Store(context);
        var category = await store.CreateCategoryAsync(Business, 5, new ExpenseCategoryCommandInput("Internet Bill", account.Id, true), CancellationToken.None);

        var id = await store.CreateAsync(Business, 5, Input(category.Id, 1500m, PettyAccount, ExpensePaymentSources.PettyCash), CancellationToken.None);
        await store.SubmitAsync(Business, 5, id, CancellationToken.None);

        await using (var pending = Context(database))
            Assert.Equal(0, await pending.JournalEntries.CountAsync());

        await store.ApproveAsync(Business, 6, id, null, CancellationToken.None);

        var lines = await LinesAsync(database, id);
        Assert.Equal(2, lines.Count);
        Assert.Contains(lines, line => line.AccountId == account.Id && line.DebitAmount == 1500m && line.CreditAmount == 0m);
        Assert.Contains(lines, line => line.AccountId == PettyAccount && line.DebitAmount == 0m && line.CreditAmount == 1500m);
    });

    [Fact]
    public async Task BankExpense_WithVat_KeepsTheAcceptedThreeLinePostingBalanced() => await RunAsync(async database =>
    {
        await using var context = Context(database);
        var store = Store(context);
        var input = Input(1, 115m, BankAccountId, ExpensePaymentSources.Bank) with { VatAmount = 15m, VatRate = 15m };
        var id = await store.CreateAsync(Business, 5, input, CancellationToken.None);
        await store.SubmitAsync(Business, 5, id, CancellationToken.None);

        await store.ApproveAsync(Business, 6, id, null, CancellationToken.None);

        var lines = await LinesAsync(database, id);
        Assert.Equal(3, lines.Count);
        Assert.Contains(lines, line => line.AccountId == 7100 && line.DebitAmount == 100m);
        Assert.Contains(lines, line => line.AccountId == VatAccount && line.DebitAmount == 15m);
        Assert.Contains(lines, line => line.AccountId == BankAccountId && line.CreditAmount == 115m);
        Assert.Equal(lines.Sum(line => line.DebitAmount), lines.Sum(line => line.CreditAmount));
    });

    [Fact]
    public async Task PosDrawerExpense_CreditsTheOutletsMappedCashAccountOnly() => await RunAsync(async database =>
    {
        await using var context = Context(database);
        var store = Store(context);

        var wrong = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.CreateAsync(Business, 5, Input(1, 40m, BankAccountId, ExpensePaymentSources.PosDrawer), CancellationToken.None));
        Assert.Contains("POS Drawer expense must credit the outlet's cash account", wrong.Message);

        var id = await store.CreateAsync(Business, 5, Input(1, 40m, CashAccount, ExpensePaymentSources.PosDrawer), CancellationToken.None);
        await store.SubmitAsync(Business, 5, id, CancellationToken.None);
        await store.ApproveAsync(Business, 6, id, null, CancellationToken.None);

        var lines = await LinesAsync(database, id);
        Assert.Contains(lines, line => line.AccountId == CashAccount && line.CreditAmount == 40m);
        Assert.DoesNotContain(lines, line => line.AccountId == BankAccountId);
    });

    [Fact]
    public async Task OutletCashOverride_ReplacesTheBusinessDrawerAccount() => await RunAsync(async database =>
    {
        await using var context = Context(database);
        context.ChartAccounts.Add(Account(1030, Business, "1030", "Outlet Cash", ChartAccountTypes.Asset));
        await context.SaveChangesAsync();
        context.AccountingOutletConfigurations.Add(new AccountingOutletConfiguration { BusinessId = Business, OutletId = Outlet, OutletCashAccountId = 1030 });
        await context.SaveChangesAsync();
        var store = Store(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.CreateAsync(Business, 5, Input(1, 40m, CashAccount, ExpensePaymentSources.PosDrawer), CancellationToken.None));

        // The override is reachable without the funding flag because configuration chose it.
        Assert.True(await store.CreateAsync(Business, 5, Input(1, 40m, 1030, ExpensePaymentSources.PosDrawer), CancellationToken.None) > 0);
    });

    [Fact]
    public async Task IncompatiblePendingExpense_IsRefusedAtApproval_WithoutAJournal_AndCanBeCorrected() => await RunAsync(async database =>
    {
        long id;
        await using (var setup = Context(database))
        {
            var pending = new Expense
            {
                BusinessId = Business, Category = "Utilities", ExpenseCategoryId = 1, Amount = 40m, TaxableAmount = 40m,
                PaymentAccountId = BankAccountId, PaymentSource = ExpensePaymentSources.PosDrawer, ExpenseDate = Date, OutletId = Outlet,
                Status = ExpenseStatuses.Submitted, ExpenseNumber = "LEGACY-1", CreatedBy = 5, SubmittedBy = 5
            };
            setup.Expenses.Add(pending);
            await setup.SaveChangesAsync();
            id = pending.Id;
        }

        await using var context = Context(database);
        var store = Store(context);

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApproveAsync(Business, 6, id, null, CancellationToken.None));
        Assert.Contains("POS Drawer", refused.Message);

        await using (var verify = Context(database))
        {
            Assert.Equal(ExpenseStatuses.Submitted, (await verify.Expenses.AsNoTracking().SingleAsync(row => row.Id == id)).Status);
            Assert.Equal(0, await verify.JournalEntries.CountAsync());
        }

        // The documented resolution: reject it, correct it, submit it again.
        await store.RejectAsync(Business, 6, id, "Wrong account", CancellationToken.None);
        await store.UpdateAsync(Business, 5, id, UpdateInput(1, 40m, CashAccount, ExpensePaymentSources.PosDrawer), CancellationToken.None);
        await store.SubmitAsync(Business, 5, id, CancellationToken.None);
        await store.ApproveAsync(Business, 6, id, null, CancellationToken.None);

        Assert.Contains(await LinesAsync(database, id), line => line.AccountId == CashAccount && line.CreditAmount == 40m);
    });

    [Theory]
    [InlineData(SalesDiscount)]
    [InlineData(ForeignExpense)]
    public async Task IneligibleApprovalOverride_RollsBackAndLeavesTheExpenseSubmitted(long overrideAccount) => await RunAsync(async database =>
    {
        await using var context = Context(database);
        var store = Store(context);
        var id = await store.CreateAsync(Business, 5, Input(1, 40m, BankAccountId, ExpensePaymentSources.Bank), CancellationToken.None);
        await store.SubmitAsync(Business, 5, id, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApproveAsync(Business, 6, id, overrideAccount, CancellationToken.None));

        await using var verify = Context(database);
        Assert.Equal(ExpenseStatuses.Submitted, (await verify.Expenses.AsNoTracking().SingleAsync(row => row.Id == id)).Status);
        Assert.Equal(0, await verify.JournalEntries.CountAsync());
    });

    [Fact]
    public async Task RepeatedApproval_NeverPostsASecondJournal() => await RunAsync(async database =>
    {
        await using var context = Context(database);
        var store = Store(context);
        var id = await store.CreateAsync(Business, 5, Input(1, 40m, BankAccountId, ExpensePaymentSources.Bank), CancellationToken.None);
        await store.SubmitAsync(Business, 5, id, CancellationToken.None);
        await store.ApproveAsync(Business, 6, id, null, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApproveAsync(Business, 6, id, null, CancellationToken.None));

        await using var verify = Context(database);
        Assert.Equal(1, await verify.JournalEntries.CountAsync(journal => journal.ReferenceType == "Expense" && journal.ReferenceId == id.ToString()));
    });

    [Fact]
    public async Task RemappingACategory_ReachesPendingExpensesOnly_AndNeverAPostedJournal() => await RunAsync(async database =>
    {
        await using var context = Context(database);
        context.ChartAccounts.Add(Account(7200, Business, "7200", "New Utilities", ChartAccountTypes.Expense));
        await context.SaveChangesAsync();
        var store = Store(context);

        var posted = await store.CreateAsync(Business, 5, Input(1, 10m, BankAccountId, ExpensePaymentSources.Bank), CancellationToken.None);
        await store.SubmitAsync(Business, 5, posted, CancellationToken.None);
        await store.ApproveAsync(Business, 6, posted, null, CancellationToken.None);
        var pending = await store.CreateAsync(Business, 5, Input(1, 20m, BankAccountId, ExpensePaymentSources.Bank), CancellationToken.None);
        await store.SubmitAsync(Business, 5, pending, CancellationToken.None);

        await store.UpdateCategoryAsync(Business, 5, 1, new ExpenseCategoryCommandInput("Utilities", 7200, true), CancellationToken.None);
        await store.ApproveAsync(Business, 6, pending, null, CancellationToken.None);

        Assert.Contains(await LinesAsync(database, posted), line => line.AccountId == 7100 && line.DebitAmount == 10m);
        Assert.Contains(await LinesAsync(database, pending), line => line.AccountId == 7200 && line.DebitAmount == 20m);
        await using var verify = Context(database);
        Assert.Equal(7100, (await verify.Expenses.AsNoTracking().SingleAsync(row => row.Id == posted)).ExpenseAccountId);
    });

    [Fact]
    public async Task LegacyCategoryMapping_SurvivesMetadataEdits_ButCannotBeNewlyChosenOrReactivated() => await RunAsync(async database =>
    {
        await using var context = Context(database);
        context.ExpenseCategories.Add(new ExpenseCategory { Id = 2, BusinessId = Business, Name = "Discounts", ExpenseAccountId = SalesDiscount, IsActive = true });
        await context.SaveChangesAsync();
        var store = Store(context);

        var renamed = await store.UpdateCategoryAsync(Business, 5, 2, new ExpenseCategoryCommandInput("Old discounts", SalesDiscount, true), CancellationToken.None);
        Assert.Equal(SalesDiscount, renamed.ExpenseAccountId);

        var retired = await store.UpdateCategoryAsync(Business, 5, 2, new ExpenseCategoryCommandInput("Old discounts", SalesDiscount, false), CancellationToken.None);
        Assert.False(retired.IsActive);

        var reactivate = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.UpdateCategoryAsync(Business, 5, 2, new ExpenseCategoryCommandInput("Old discounts", SalesDiscount, true), CancellationToken.None));
        Assert.Contains("reserved for automatic postings", reactivate.Message);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.CreateCategoryAsync(Business, 5, new ExpenseCategoryCommandInput("New discounts", SalesDiscount, true), CancellationToken.None));
    });

    [Fact]
    public async Task CategoryMapping_RejectsAForeignBusinessAccount_WithoutRevealingIt() => await RunAsync(async database =>
    {
        await using var context = Context(database);
        var store = Store(context);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.CreateCategoryAsync(Business, 5, new ExpenseCategoryCommandInput("Stolen", ForeignExpense, true), CancellationToken.None));

        Assert.Equal($"Expense account {ForeignExpense} was not found in this business.", error.Message);
    });

    [Fact]
    public async Task ConcurrentDeactivationAndCategoryMapping_NeverLeaveAnActiveCategoryOnAnInactiveAccount() => await RunAsync(async database =>
    {
        await using (var setup = Context(database))
        {
            setup.ChartAccounts.Add(Account(7300, Business, "7300", "Contested", ChartAccountTypes.Expense));
            await setup.SaveChangesAsync();
        }

        async Task<bool> Attempt(Func<RetailPOSDbContext, Task> action)
        {
            await using var context = Context(database);
            try
            {
                await action(context);
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        await Task.WhenAll(
            Attempt(context => new ChartAccountCommandStore(context, null, NullLogger<ChartAccountCommandStore>.Instance)
                .DeactivateAsync(Business, 5, 7300, CancellationToken.None)),
            Attempt(context => Store(context).CreateCategoryAsync(Business, 5, new ExpenseCategoryCommandInput("Contested", 7300, true), CancellationToken.None)));

        await using var verify = Context(database);
        var accountActive = (await verify.ChartAccounts.AsNoTracking().SingleAsync(account => account.Id == 7300)).IsActive;
        var hasActiveCategory = await verify.ExpenseCategories.AnyAsync(category => category.ExpenseAccountId == 7300 && category.IsActive);
        Assert.False(hasActiveCategory && !accountActive);
    });

    private static ExpenseCreationInput Input(long categoryId, decimal amount, long paymentAccountId, string source) => new(
        categoryId, amount, null, null, null, null, null, paymentAccountId, null, null, null, Date, Outlet, source);

    private static ExpenseUpdateInput UpdateInput(long categoryId, decimal amount, long paymentAccountId, string source) => new(
        categoryId, amount, null, null, null, null, null, paymentAccountId, null, null, null, Date, Outlet, source);

    private static async Task<List<JournalEntryLine>> LinesAsync(PostgresTestDatabase database, long expenseId)
    {
        await using var context = Context(database);
        var journalId = (await context.Expenses.AsNoTracking().SingleAsync(row => row.Id == expenseId)).JournalEntryId;
        Assert.NotNull(journalId);
        Assert.Equal(JournalEntryStatuses.Posted, (await context.JournalEntries.AsNoTracking().SingleAsync(row => row.Id == journalId)).Status);
        return await context.JournalEntryLines.AsNoTracking().Where(line => line.JournalEntryId == journalId).ToListAsync();
    }

    private static ExpenseCommandStore Store(RetailPOSDbContext context)
    {
        var tenant = new Mock<ITenantAccessService>();
        tenant.Setup(value => value.RequireBusinessId()).Returns(Business);
        var audit = new Mock<IAuditService>();
        audit.Setup(value => value.RecordAsync(It.IsAny<AuditEventInput>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var resolver = new Mock<IExpensePostingAccountResolver>();
        resolver
            .Setup(value => value.ResolveAccountIdAsync(Business, "supplier_payable", It.IsAny<long?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SupplierPayable);
        resolver
            .Setup(value => value.ResolveAccountIdAsync(Business, "vat_receivable", It.IsAny<long?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(VatAccount);
        var journalStore = new ManualJournalDraftStore(
            context, audit.Object, new JournalNumberAllocator(context), NullLogger<ManualJournalDraftStore>.Instance);

        return new ExpenseCommandStore(
            context,
            audit.Object,
            NullLogger<ExpenseCommandStore>.Instance,
            new ExpenseApprovalJournalPoster(SupplierPaymentCreationTestHarness.CreateJournalCommandSender(journalStore), tenant.Object),
            resolver.Object,
            new StubMakerCheckerPolicy(),
            new AllowAllExpenseLocations(),
            new SequentialExpenseNumbers());
    }

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
                    Seed(setup);
                    await setup.SaveChangesAsync();

                    // The seed sets explicit IDs, which do not advance the identity sequence.
                    await setup.Database.ExecuteSqlRawAsync("SELECT setval(pg_get_serial_sequence('expense_categories', 'id'), 100)");
                }

                await test(database);
            });

    private static void Seed(RetailPOSDbContext context)
    {
        context.Businesses.AddRange(new Business { Id = Business, Name = "Own" }, new Business { Id = 2, Name = "Other" });
        context.Outlets.Add(new Outlet { Id = Outlet, BusinessId = Business, Name = "Dhaka", Address = "X", InvoicePrefix = "DHK01" });
        context.ChartAccounts.AddRange(
            Account(CashAccount, Business, "1000", "Cash on Hand", ChartAccountTypes.Asset, flagged: true),
            Account(PettyAccount, Business, "1010", "Petty Cash", ChartAccountTypes.Asset, flagged: true),
            Account(BankAccountId, Business, "1020", "Bank Account", ChartAccountTypes.Asset, flagged: true),
            Account(VatAccount, Business, "1300", "Input VAT", ChartAccountTypes.Asset),
            Account(SupplierPayable, Business, "2000", "Supplier Payables", ChartAccountTypes.Liability),
            Account(SalesDiscount, Business, "4010", "Sales Discounts", ChartAccountTypes.Expense, system: true, manual: false),
            Account(7100, Business, "7100", "Utilities Expense", ChartAccountTypes.Expense),
            Account(ForeignExpense, 2, "7100", "Foreign Expense", ChartAccountTypes.Expense));
        context.ExpenseCategories.Add(new ExpenseCategory { Id = 1, BusinessId = Business, Name = "Utilities", ExpenseAccountId = 7100, IsActive = true });
        context.AccountingConfigurations.Add(new AccountingConfiguration
        {
            BusinessId = Business, CashAccountId = CashAccount, PettyCashAccountId = PettyAccount, DefaultBankAccountId = BankAccountId,
            SupplierPayableAccountId = SupplierPayable, VatReceivableAccountId = VatAccount, JournalNumberPrefix = "JR",
            JournalNumberPadding = 6, JournalSequenceResetPolicy = AccountingSequenceResetPolicies.PerFinancialYear
        });
        context.FinancialYears.Add(new FinancialYear
        {
            Id = 1, BusinessId = Business, YearCode = "FY26", Name = "FY26", IsCurrent = true,
            StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2026, 12, 31)
        });
        context.AccountingPeriods.Add(new AccountingPeriod
        {
            Id = 1, FinancialYearId = 1, PeriodNumber = 3, Name = "March", Status = AccountingPeriodStatuses.Open,
            StartDate = new DateTime(2026, 3, 1), EndDate = new DateTime(2026, 3, 31)
        });
    }

    private static ChartAccount Account(long id, long businessId, string code, string name, string type, bool flagged = false, bool system = false, bool manual = true) => new()
    {
        Id = id, BusinessId = businessId, AccountCode = code, AccountName = name, AccountType = type,
        NormalBalanceType = type is ChartAccountTypes.Asset or ChartAccountTypes.Expense ? NormalBalanceTypes.Debit : NormalBalanceTypes.Credit,
        IsActive = true, IsExpensePaymentAccount = flagged, IsSystemAccount = system, AllowManualPosting = manual
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
