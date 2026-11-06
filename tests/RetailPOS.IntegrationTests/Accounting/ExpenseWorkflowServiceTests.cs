using RetailPOS.Application.AccountingJournals.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RetailPOS.Application.Expenses.Abstractions;
using RetailPOS.Application.Expenses.Commands;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Expenses;

namespace RetailPOS.Tests.Accounting;

public class ExpenseCommandWorkflowTests : IDisposable
{
    private readonly RetailPOSDbContext _db;
    private readonly Mock<IAuditService> _audit = new();
    private readonly Mock<IExpenseApprovalJournalPoster> _journalPoster = new();
    private readonly Mock<IExpensePostingAccountResolver> _accountResolver = new();
    private readonly List<AuditEventInput> _auditEvents = new();
    private readonly List<ExpenseApprovalJournalInput> _postedJournals = new();

    public ExpenseCommandWorkflowTests()
    {
        var options = new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _db = new RetailPOSDbContext(options);
        Seed();

        _audit
            .Setup(x => x.RecordAsync(It.IsAny<AuditEventInput>(), It.IsAny<CancellationToken>()))
            .Callback<AuditEventInput, CancellationToken>((input, _) => _auditEvents.Add(input))
            .Returns(Task.CompletedTask);

        _journalPoster
            .Setup(x => x.CreateOrGetAndPostAsync(
                It.IsAny<long>(),
                It.IsAny<long>(),
                It.IsAny<ExpenseApprovalJournalInput>(),
                It.IsAny<CancellationToken>()))
            .Callback<long, long, ExpenseApprovalJournalInput, CancellationToken>(
                (_, _, input, _) => _postedJournals.Add(input))
            .ReturnsAsync(9301);

        _accountResolver
            .Setup(x => x.ResolveAccountIdAsync(
                It.IsAny<long>(),
                It.IsAny<string>(),
                It.IsAny<long?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(7001);

        _audit
            .Setup(x => x.RecordFailureAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task ApproveCommand_PostsJournal_FromCategoryMapping()
    {
        var store = CreateStore();
        var category = await new CreateExpenseCategoryCommandHandler(store).Handle(
            new CreateExpenseCategoryCommand(
                1, 101, new ExpenseCategoryCommandInput("Utilities", 7001, true)),
            CancellationToken.None);
        var expenseId = await new CreateExpenseCommandHandler(store).Handle(
            new CreateExpenseCommand(
                1,
                101,
                new ExpenseCreationInput(
                    category.Id, 150m, null, null, null, null, null, 1021,
                    null, "Electricity bill", null,
                    new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc), 11)),
            CancellationToken.None);

        await new SubmitExpenseCommandHandler(store).Handle(
            new SubmitExpenseCommand(1, 101, expenseId), CancellationToken.None);
        await new ApproveExpenseCommandHandler(store).Handle(
            new ApproveExpenseCommand(1, 101, expenseId, null), CancellationToken.None);

        var approved = await _db.Expenses.SingleAsync(x => x.Id == expenseId);
        Assert.Equal(ExpenseStatuses.Approved, approved.Status);
        Assert.Equal(7001, approved.ExpenseAccountId);
        Assert.Equal(9301, approved.JournalEntryId);

        var journal = Assert.Single(_postedJournals);
        Assert.Equal("Expense", journal.ReferenceType);
        Assert.Equal(expenseId.ToString(), journal.ReferenceId);
        Assert.Contains(journal.Lines, x => x.AccountId == 7001 && x.DebitAmount == 150m && x.CreditAmount == 0m);
        Assert.Contains(journal.Lines, x => x.AccountId == 1021 && x.DebitAmount == 0m && x.CreditAmount == 150m);

        Assert.Contains(_auditEvents, x => x.PrimaryEntity?.Type == "Expense"
            && x.PrimaryEntity?.Id == expenseId.ToString()
            && x.ActionType == AuditActionType.Update
            && x.Entities.Single().Fields.Any(f => f.FieldName == "JournalEntryId" && Equals(f.NewValue, 9301L)));
    }

    [Fact]
    public async Task UpdateAsync_AuditsMaterialAmountVatAndAccountChanges()
    {
        var store = CreateStore();
        var category = await new CreateExpenseCategoryCommandHandler(store).Handle(
            new CreateExpenseCategoryCommand(1, 101, new ExpenseCategoryCommandInput("Supplies", 7001, true)),
            CancellationToken.None);
        var expenseId = await new CreateExpenseCommandHandler(store).Handle(
            new CreateExpenseCommand(1, 101, new ExpenseCreationInput(
                category.Id, 100m, 5m, 5m, "VAT5", false, null, 1021,
                "EXP-AUD-1", null, null,
                new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc), 11)),
            CancellationToken.None);

        await new UpdateExpenseCommandHandler(store).Handle(
            new UpdateExpenseCommand(1, 102, expenseId, new ExpenseUpdateInput(
                category.Id, 120m, 10m, 10m, "VAT10", true, null, 1001,
                "EXP-AUD-2", null, null,
                new DateTime(2026, 3, 16, 0, 0, 0, DateTimeKind.Utc), 11, ExpensePaymentSources.PettyCash)),
            CancellationToken.None);

        var updateAudit = Assert.Single(_auditEvents, x => x.PrimaryEntity?.Type == "Expense" && x.PrimaryEntity?.Id == expenseId.ToString() && x.Summary.Contains("updated", StringComparison.Ordinal));
        var fields = updateAudit.Entities.Single().Fields;
        Assert.Contains(fields, x => x.FieldName == "Amount" && Equals(x.OldValue, 100m) && Equals(x.NewValue, 120m));
        Assert.Contains(fields, x => x.FieldName == "VatAmount" && Equals(x.OldValue, 5m) && Equals(x.NewValue, 10m));
        Assert.Contains(fields, x => x.FieldName == "VatRate" && Equals(x.OldValue, 5m) && Equals(x.NewValue, 10m));
        Assert.Contains(fields, x => x.FieldName == "PaymentAccountId" && Equals(x.OldValue, 1021L) && Equals(x.NewValue, 1001L));
        Assert.Contains(fields, x => x.FieldName == "TaxCode" && Equals(x.OldValue, "VAT5") && Equals(x.NewValue, "VAT10"));
    }

    [Fact]
    public async Task ApproveAsync_Rejects_WhenPostingPeriodIsClosed()
    {
        var period = await _db.AccountingPeriods.FirstAsync(x => x.Id == 9002);
        period.Status = AccountingPeriodStatuses.Closed;
        period.ClosedAt = DateTime.UtcNow;
        period.ClosedBy = 101;
        await _db.SaveChangesAsync();

        var store = CreateStore();
        var category = await new CreateExpenseCategoryCommandHandler(store).Handle(
            new CreateExpenseCategoryCommand(1, 101, new ExpenseCategoryCommandInput("Supplies", 7001, true)),
            CancellationToken.None);
        var expenseId = await new CreateExpenseCommandHandler(store).Handle(
            new CreateExpenseCommand(1, 101, new ExpenseCreationInput(
                category.Id, 40m, null, null, null, null, null, 1021,
                null, "Office supplies", null,
                new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc), null)),
            CancellationToken.None);
        await new SubmitExpenseCommandHandler(store).Handle(
            new SubmitExpenseCommand(1, 101, expenseId), CancellationToken.None);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ApproveExpenseCommandHandler(store).Handle(
                new ApproveExpenseCommand(1, 101, expenseId, null), CancellationToken.None));

        Assert.Contains("accounting period", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await _db.JournalEntries.CountAsync(x => x.ReferenceType == "Expense"));
        Assert.Empty(_postedJournals);
    }

    [Fact]
    public async Task RejectAndCancel_DoNotCreateJournals()
    {
        var store = CreateStore();
        var category = await new CreateExpenseCategoryCommandHandler(store).Handle(
            new CreateExpenseCategoryCommand(1, 101, new ExpenseCategoryCommandInput("Rent", 7001, true)),
            CancellationToken.None);
        var rejectedId = await new CreateExpenseCommandHandler(store).Handle(
            new CreateExpenseCommand(1, 101, new ExpenseCreationInput(
                category.Id, 900m, null, null, null, null, null, 1021,
                null, "Shop rent", null,
                new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc), null)),
            CancellationToken.None);
        await new SubmitExpenseCommandHandler(store).Handle(
            new SubmitExpenseCommand(1, 101, rejectedId), CancellationToken.None);
        await new RejectExpenseCommandHandler(store).Handle(
            new RejectExpenseCommand(1, 101, rejectedId, "Receipt mismatch"), CancellationToken.None);

        var cancelledId = await new CreateExpenseCommandHandler(store).Handle(
            new CreateExpenseCommand(1, 101, new ExpenseCreationInput(
                category.Id, 55m, null, null, null, null, null, 1021,
                null, "Taxi", null,
                new DateTime(2026, 3, 11, 0, 0, 0, DateTimeKind.Utc), null)),
            CancellationToken.None);
        await new CancelExpenseCommandHandler(store).Handle(
            new CancelExpenseCommand(1, 101, cancelledId, "Created in error"), CancellationToken.None);

        Assert.Equal(ExpenseStatuses.Rejected, (await _db.Expenses.SingleAsync(x => x.Id == rejectedId)).Status);
        Assert.Equal(ExpenseStatuses.Cancelled, (await _db.Expenses.SingleAsync(x => x.Id == cancelledId)).Status);
        Assert.Null((await _db.Expenses.SingleAsync(x => x.Id == rejectedId)).JournalEntryId);
        Assert.Null((await _db.Expenses.SingleAsync(x => x.Id == cancelledId)).JournalEntryId);
        Assert.Equal(0, await _db.JournalEntries.CountAsync(x => x.ReferenceType == "Expense"));
    }

    [Fact]
    public async Task Approve_CarriesTheWarehouseOntoTheJournalWithoutChangingThePostingShape()
    {
        _db.Warehouses.Add(new Warehouse { Id = 21, BusinessId = 1, Name = "Central", Address = "X" });
        await _db.SaveChangesAsync();
        var store = CreateStore();
        var category = await new CreateExpenseCategoryCommandHandler(store).Handle(
            new CreateExpenseCategoryCommand(1, 101, new ExpenseCategoryCommandInput("Freight", 7001, true)),
            CancellationToken.None);
        var expenseId = await new CreateExpenseCommandHandler(store).Handle(
            new CreateExpenseCommand(1, 101, new ExpenseCreationInput(
                category.Id, 40m, null, null, null, null, null, 1021,
                null, "Freight", null,
                new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc), null, null, 21)),
            CancellationToken.None);
        await new SubmitExpenseCommandHandler(store).Handle(new SubmitExpenseCommand(1, 101, expenseId), CancellationToken.None);

        await new ApproveExpenseCommandHandler(store).Handle(
            new ApproveExpenseCommand(1, 101, expenseId, null),
            CancellationToken.None);

        var journal = Assert.Single(_postedJournals);
        Assert.Equal(21, journal.WarehouseId);
        Assert.Null(journal.OutletId);
        Assert.All(journal.Lines, line => Assert.Equal(21, line.WarehouseId));
        Assert.Equal(40m, journal.Lines.Single(line => line.AccountId == 7001).DebitAmount);
        Assert.Equal(40m, journal.Lines.Single(line => line.AccountId == 1021).CreditAmount);
    }

    private ExpenseCommandStore CreateStore() => new(
        _db,
        _audit.Object,
        NullLogger<ExpenseCommandStore>.Instance,
        _journalPoster.Object,
        _accountResolver.Object,
        new RetailPOS.IntegrationTests.Infrastructure.StubMakerCheckerPolicy(),
            new RetailPOS.IntegrationTests.Infrastructure.AllowAllExpenseLocations(),
            new RetailPOS.IntegrationTests.Infrastructure.SequentialExpenseNumbers());

    private void Seed()
    {
        _db.Businesses.Add(new Business
        {
            Id = 1,
            Name = "Expense Workflow Business",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        _db.Outlets.Add(new Outlet
        {
            Id = 11,
            BusinessId = 1,
            Name = "Main Outlet",
            Address = "X",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        _db.ChartAccounts.AddRange(
            Account(1001, "1000", "Cash", ChartAccountTypes.Asset, NormalBalanceTypes.Debit),
            Account(1021, "1020", "Bank", ChartAccountTypes.Asset, NormalBalanceTypes.Debit),
            Account(2001, "2000", "Supplier Payable", ChartAccountTypes.Liability, NormalBalanceTypes.Credit),
            Account(3001, "3000", "Owner Capital", ChartAccountTypes.Equity, NormalBalanceTypes.Credit),
            Account(4001, "4000", "Sales", ChartAccountTypes.Income, NormalBalanceTypes.Credit),
            Account(4011, "4010", "Sales Discount", ChartAccountTypes.Expense, NormalBalanceTypes.Debit),
            Account(4021, "4020", "Sales Return", ChartAccountTypes.Expense, NormalBalanceTypes.Debit),
            Account(5001, "5000", "COGS", ChartAccountTypes.CostOfGoodsSold, NormalBalanceTypes.Debit),
            Account(5101, "5100", "Inventory Shortage", ChartAccountTypes.Expense, NormalBalanceTypes.Debit),
            Account(5201, "5200", "Inventory Gain", ChartAccountTypes.Income, NormalBalanceTypes.Credit),
            Account(6001, "6000", "Other Income", ChartAccountTypes.Income, NormalBalanceTypes.Credit),
            Account(6101, "6100", "Rounding Difference", ChartAccountTypes.Expense, NormalBalanceTypes.Debit),
            Account(6201, "6200", "Cash Difference Gain", ChartAccountTypes.Income, NormalBalanceTypes.Credit),
            Account(6211, "6210", "Cash Difference Loss", ChartAccountTypes.Expense, NormalBalanceTypes.Debit),
            Account(7001, "7000", "Utilities Expense", ChartAccountTypes.Expense, NormalBalanceTypes.Debit)
        );

        _db.AccountingConfigurations.Add(new AccountingConfiguration
        {
            BusinessId = 1,
            CashAccountId = 1001,
            PettyCashAccountId = 1001,
            DefaultBankAccountId = 1021,
            InventoryAccountId = 1001,
            SalesAccountId = 4001,
            SalesDiscountAccountId = 4011,
            SalesReturnAccountId = 4021,
            CogsAccountId = 5001,
            InventoryShortageAccountId = 5101,
            InventoryGainAccountId = 5201,
            SupplierPayableAccountId = 2001,
            CustomerAdvanceAccountId = 2001,
            OtherIncomeAccountId = 6001,
            OwnerCapitalAccountId = 3001,
            OwnerDrawingsAccountId = 3001,
            RoundingDifferenceAccountId = 6101,
            CashDifferenceGainAccountId = 6201,
            CashDifferenceLossAccountId = 6211,
            JournalNumberPrefix = "JR",
            JournalNumberPadding = 6,
            JournalSequenceResetPolicy = AccountingSequenceResetPolicies.PerFinancialYear,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        _db.FinancialYears.Add(new FinancialYear
        {
            Id = 9001,
            BusinessId = 1,
            YearCode = "FY26",
            Name = "FY26",
            StartDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc),
            IsCurrent = true,
            IsClosed = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        _db.AccountingPeriods.Add(new AccountingPeriod
        {
            Id = 9002,
            FinancialYearId = 9001,
            PeriodNumber = 3,
            Name = "March",
            StartDate = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2026, 3, 31, 0, 0, 0, DateTimeKind.Utc),
            Status = AccountingPeriodStatuses.Open,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        _db.SaveChanges();
    }

    private static ChartAccount Account(long id, string code, string name, string type, string normalBalance)
    {
        return new ChartAccount
        {
            Id = id,
            BusinessId = 1,
            AccountCode = code,
            AccountName = name,
            AccountType = type,
            NormalBalanceType = normalBalance,
            IsSystemAccount = false,
            IsControlAccount = false,
            AllowManualPosting = true,
            IsActive = true,
            IsExpensePaymentAccount = id is 1001 or 1021,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }
}