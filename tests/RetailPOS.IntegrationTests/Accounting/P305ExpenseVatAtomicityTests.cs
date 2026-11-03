using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RetailPOS.API.Services;
using RetailPOS.Application.AccountingJournals.Abstractions;
using RetailPOS.Application.Expenses.Abstractions;
using RetailPOS.Application.Expenses.Commands;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.AccountingJournals;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Expenses;
using Xunit;

namespace RetailPOS.Tests.Accounting;

public sealed class P305ExpenseVatAtomicityTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly ThrowOnExpenseApprovalSaveInterceptor _interceptor = new();
    private readonly Mock<ITenantAccessService> _tenant = new();
    private static int s_journalNumber;

    public P305ExpenseVatAtomicityTests()
    {
        _connection.Open();
        using var db = CreateContext();
        db.Database.EnsureCreated();
        Seed(db);
        _tenant.Setup(x => x.RequireBusinessId()).Returns(1L);
        _tenant.Setup(x => x.IsSuperAdmin).Returns(false);
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task VatExpenseApproval_RollsBackAfterJournalSave_ThenRetriesExactlyOnce()
    {
        long expenseId;
        using (var db = CreateContext())
        {
            var store = ApproveStore(db);
            expenseId = await new CreateExpenseCommandHandler(store).Handle(
                new CreateExpenseCommand(1, 1, new ExpenseCreationInput(
                    1, 110m, 10m, 10m, "VAT", false, null, 101,
                    null, "VAT expense", null, new DateTime(2026, 3, 15), null)),
                CancellationToken.None);
            await new SubmitExpenseCommandHandler(store).Handle(
                new SubmitExpenseCommand(1, 1, expenseId), CancellationToken.None);
        }

        _interceptor.Enabled = true;
        using (var db = CreateContext())
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new ApproveExpenseCommandHandler(ApproveStore(db)).Handle(
                    new ApproveExpenseCommand(1, 1, expenseId, null), CancellationToken.None));

        using (var verify = CreateContext())
        {
            var expense = await verify.Expenses.SingleAsync(x => x.Id == expenseId);
            Assert.Equal(ExpenseStatuses.Submitted, expense.Status);
            Assert.Null(expense.JournalEntryId);
            Assert.Empty(await verify.JournalEntries.Where(x => x.ReferenceType == "Expense").ToListAsync());
            Assert.Empty(await verify.JournalEntryLines.ToListAsync());
        }

        _interceptor.Enabled = false;
        using (var db = CreateContext())
            await new ApproveExpenseCommandHandler(ApproveStore(db)).Handle(
                new ApproveExpenseCommand(1, 1, expenseId, null), CancellationToken.None);

        using var final = CreateContext();
        var approved = await final.Expenses.SingleAsync(x => x.Id == expenseId);
        Assert.Equal(ExpenseStatuses.Approved, approved.Status);
        Assert.Equal(10m, approved.VatAmount);
        var journal = await final.JournalEntries.Include(x => x.Lines).SingleAsync(x => x.ReferenceType == "Expense");
        Assert.Equal(3, journal.Lines.Count);
        Assert.Contains(journal.Lines, x => x.AccountId == 201 && x.DebitAmount == 100m && x.CreditAmount == 0m);
        Assert.Contains(journal.Lines, x => x.AccountId == 202 && x.DebitAmount == 10m && x.CreditAmount == 0m);
        Assert.Contains(journal.Lines, x => x.AccountId == 101 && x.DebitAmount == 0m && x.CreditAmount == 110m);
    }

    private RetailPOSDbContext CreateContext() => new SqliteExpenseVatDbContext(new DbContextOptionsBuilder<RetailPOSDbContext>().UseSqlite(_connection).AddInterceptors(_interceptor).Options);

    private ExpenseCommandStore ApproveStore(RetailPOSDbContext db)
    {
        var numbers = new Mock<IJournalNumberAllocator>();
        numbers.Setup(x => x.AllocateNextJournalNumberAsync(
                It.IsAny<long>(),
                It.IsAny<long?>(),
                It.IsAny<DateTime>(),
                It.IsAny<long?>(),
                It.IsAny<CancellationToken>()))
            .Returns(() => Task.FromResult($"VAT-{Interlocked.Increment(ref s_journalNumber)}"));
        var audit = Mock.Of<IAuditService>(x =>
            x.RecordAsync(It.IsAny<AuditEventInput>(), It.IsAny<CancellationToken>()) == Task.CompletedTask);
        var config = AccountingConfigurationServiceTestFactory.Create(db, _tenant.Object,  NullLogger<AccountingConfigurationService>.Instance);
        var journalStore = new ManualJournalDraftStore(
            db,
            audit,
            numbers.Object,
            NullLogger<ManualJournalDraftStore>.Instance);
        var poster = new ExpenseApprovalJournalPoster(
            SupplierPaymentCreationTestHarness.CreateJournalCommandSender(journalStore),
            _tenant.Object);
        var accountResolver = new ExpenseApprovalPostingAccountResolver(config, _tenant.Object);
        return new ExpenseCommandStore(
            db,
            audit,
            NullLogger<ExpenseCommandStore>.Instance,
            poster,
            accountResolver,
            new RetailPOS.IntegrationTests.Infrastructure.StubMakerCheckerPolicy());
    }

    private static void Seed(RetailPOSDbContext db)
    {
        db.Businesses.Add(new Business { Id = 1, Name = "VAT expense business" });
        db.Outlets.Add(new Outlet { Id = 11, BusinessId = 1, Name = "Main", Address = "Test" });
        db.ChartAccounts.AddRange(Account(101, "Bank", ChartAccountTypes.Asset, NormalBalanceTypes.Debit), Account(201, "Expense", ChartAccountTypes.Expense, NormalBalanceTypes.Debit), Account(202, "Input VAT", ChartAccountTypes.Asset, NormalBalanceTypes.Debit), Account(203, "Supplier Payable", ChartAccountTypes.Liability, NormalBalanceTypes.Credit));
        db.ExpenseCategories.Add(new ExpenseCategory { Id = 1, BusinessId = 1, Name = "Utilities", ExpenseAccountId = 201, IsActive = true });
        db.AccountingConfigurations.Add(new AccountingConfiguration { Id = 1, BusinessId = 1, DefaultBankAccountId = 101, SupplierPayableAccountId = 203, VatReceivableAccountId = 202, CashAccountId = 101, InventoryAccountId = 201, SalesAccountId = 201, SalesDiscountAccountId = 201, SalesReturnAccountId = 201, CogsAccountId = 201, InventoryShortageAccountId = 201, InventoryGainAccountId = 201, CustomerAdvanceAccountId = 203, OtherIncomeAccountId = 201, OwnerCapitalAccountId = 203, OwnerDrawingsAccountId = 203, RetainedEarningsAccountId = 203, RoundingDifferenceAccountId = 201, CashDifferenceGainAccountId = 201, CashDifferenceLossAccountId = 201, JournalNumberPrefix = "VAT" });
        db.FinancialYears.Add(new FinancialYear { Id = 1, BusinessId = 1, YearCode = "FY26", Name = "2026", StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2026, 12, 31), IsCurrent = true });
        db.AccountingPeriods.Add(new AccountingPeriod { Id = 1, FinancialYearId = 1, PeriodNumber = 3, Name = "March", StartDate = new DateTime(2026, 3, 1), EndDate = new DateTime(2026, 3, 31), Status = AccountingPeriodStatuses.Open });
        db.SaveChanges();
    }

    private static ChartAccount Account(long id, string name, string type, string normal) => new() { Id = id, BusinessId = 1, AccountCode = id.ToString(), AccountName = name, AccountType = type, NormalBalanceType = normal, IsActive = true };

    private sealed class ThrowOnExpenseApprovalSaveInterceptor : SaveChangesInterceptor
    {
        public bool Enabled { get; set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Enabled && eventData.Context?.ChangeTracker.Entries<Expense>().Any(x => x.State == EntityState.Modified && x.Entity.Status == ExpenseStatuses.Approved) == true)
            {
                Enabled = false;
                throw new InvalidOperationException("Injected failure after Expense journal persistence.");
            }
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class SqliteExpenseVatDbContext(DbContextOptions<RetailPOSDbContext> options) : RetailPOSDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<JournalEntryLine>().Property(x => x.DebitAmount).HasConversion<double>();
            modelBuilder.Entity<JournalEntryLine>().Property(x => x.CreditAmount).HasConversion<double>();
        }
    }
}
