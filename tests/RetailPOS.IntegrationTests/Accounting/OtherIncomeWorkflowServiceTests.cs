using RetailPOS.Application.AccountingJournals.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RetailPOS.Application.OtherIncome.Abstractions;
using RetailPOS.Application.OtherIncome.Commands;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.OtherIncome;

namespace RetailPOS.Tests.Accounting;

public class OtherIncomeCommandWorkflowTests : IDisposable
{
    private readonly RetailPOSDbContext _db;
    private readonly Mock<IAuditService> _audit = new();
    private readonly Mock<IOtherIncomeApprovalJournalPoster> _journalPoster = new();
    private readonly Mock<IOtherIncomePostingAccountResolver> _accountResolver = new();
    private readonly List<AuditEventInput> _auditEvents = new();
    private readonly List<OtherIncomeApprovalJournalInput> _postedJournals = new();

    public OtherIncomeCommandWorkflowTests()
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
                It.IsAny<OtherIncomeApprovalJournalInput>(),
                It.IsAny<CancellationToken>()))
            .Callback<long, long, OtherIncomeApprovalJournalInput, CancellationToken>(
                (_, _, input, _) => _postedJournals.Add(input))
            .ReturnsAsync(9302);

        _accountResolver
            .Setup(x => x.ResolveAccountIdAsync(
                It.IsAny<long>(),
                It.IsAny<string>(),
                It.IsAny<long?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(6001);

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
    public async Task ApproveCommand_PostsJournal_UsingDefaultConfigurationAccount()
    {
        var store = CreateStore();
        var entryId = await new CreateOtherIncomeCommandHandler(store).Handle(
            new CreateOtherIncomeCommand(1, 101, new OtherIncomeCreationInput(
                new DateTime(2026, 4, 10, 0, 0, 0, DateTimeKind.Utc),
                "Commission", null, 125m, null, null, null, null,
                "Bank", 1021, null, "Partner rebate", 11)),
            CancellationToken.None);

        await new SubmitOtherIncomeCommandHandler(store).Handle(
            new SubmitOtherIncomeCommand(1, 101, entryId), CancellationToken.None);
        await new ApproveOtherIncomeCommandHandler(store).Handle(
            new ApproveOtherIncomeCommand(1, 101, entryId, null), CancellationToken.None);

        var approved = await _db.OtherIncomeEntries.SingleAsync(x => x.Id == entryId);
        Assert.Equal(OtherIncomeStatuses.Approved, approved.Status);
        Assert.Equal(6001, approved.IncomeAccountId);
        Assert.Equal(9302, approved.JournalEntryId);

        var journal = Assert.Single(_postedJournals);
        Assert.Equal("OtherIncome", journal.ReferenceType);
        Assert.Equal(entryId.ToString(), journal.ReferenceId);
        Assert.Contains(journal.Lines, x => x.AccountId == 1021 && x.DebitAmount == 125m && x.CreditAmount == 0m);
        Assert.Contains(journal.Lines, x => x.AccountId == 6001 && x.DebitAmount == 0m && x.CreditAmount == 125m);

        Assert.Contains(_auditEvents, x => x.PrimaryEntity?.Type == "OtherIncomeEntry"
            && x.PrimaryEntity?.Id == entryId.ToString()
            && x.ActionType == AuditActionType.Update
            && x.Entities.Single().Fields.Any(f => f.FieldName == "JournalEntryId" && Equals(f.NewValue, 9302L)));
    }

    [Fact]
    public async Task UpdateAsync_AuditsMaterialAmountVatAndAccountChanges()
    {
        var store = CreateStore();
        var entryId = await new CreateOtherIncomeCommandHandler(store).Handle(
            new CreateOtherIncomeCommand(1, 101, new OtherIncomeCreationInput(
                new DateTime(2026, 4, 10, 0, 0, 0, DateTimeKind.Utc),
                "Commission", 6001, 100m, 5m, 5m, "VAT5", false,
                null, 1021, "INC-AUD-1", null, 11)),
            CancellationToken.None);

        await new UpdateOtherIncomeCommandHandler(store).Handle(
            new UpdateOtherIncomeCommand(1, 102, entryId, new OtherIncomeCreationInput(
                new DateTime(2026, 4, 11, 0, 0, 0, DateTimeKind.Utc),
                "Rebate", 5201, 130m, 10m, 10m, "VAT10", true,
                null, 1001, "INC-AUD-2", null, 11)),
            CancellationToken.None);

        var updateAudit = Assert.Single(_auditEvents, x => x.PrimaryEntity?.Type == "OtherIncomeEntry" && x.PrimaryEntity?.Id == entryId.ToString() && x.Summary.Contains("updated", StringComparison.Ordinal));
        var fields = updateAudit.Entities.Single().Fields;
        Assert.Contains(fields, x => x.FieldName == "Amount" && Equals(x.OldValue, 100m) && Equals(x.NewValue, 130m));
        Assert.Contains(fields, x => x.FieldName == "VatAmount" && Equals(x.OldValue, 5m) && Equals(x.NewValue, 10m));
        Assert.Contains(fields, x => x.FieldName == "VatRate" && Equals(x.OldValue, 5m) && Equals(x.NewValue, 10m));
        Assert.Contains(fields, x => x.FieldName == "IncomeAccountId" && Equals(x.OldValue, 6001L) && Equals(x.NewValue, 5201L));
        Assert.Contains(fields, x => x.FieldName == "PaymentAccountId" && Equals(x.OldValue, 1021L) && Equals(x.NewValue, 1001L));
        Assert.Contains(fields, x => x.FieldName == "TaxCode" && Equals(x.OldValue, "VAT5") && Equals(x.NewValue, "VAT10"));
    }

    [Fact]
    public async Task ApproveAsync_Rejects_WhenNoMatchingPeriod_AndLeavesNoDraftJournal()
    {
        var store = CreateStore();
        var entryId = await new CreateOtherIncomeCommandHandler(store).Handle(
            new CreateOtherIncomeCommand(1, 101, new OtherIncomeCreationInput(
                new DateTime(2026, 5, 10, 0, 0, 0, DateTimeKind.Utc),
                "Rebate", null, 45m, null, null, null, null,
                null, 1021, null, null, null)),
            CancellationToken.None);
        await new SubmitOtherIncomeCommandHandler(store).Handle(
            new SubmitOtherIncomeCommand(1, 101, entryId), CancellationToken.None);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ApproveOtherIncomeCommandHandler(store).Handle(
                new ApproveOtherIncomeCommand(1, 101, entryId, null), CancellationToken.None));

        Assert.Contains("does not belong to any accounting period", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await _db.JournalEntries.CountAsync(x => x.ReferenceType == "OtherIncome"));
        Assert.Empty(_postedJournals);
    }

    [Fact]
    public async Task RejectAndCancel_DoNotCreateJournals()
    {
        var store = CreateStore();
        var rejectedId = await new CreateOtherIncomeCommandHandler(store).Handle(
            new CreateOtherIncomeCommand(1, 101, new OtherIncomeCreationInput(
                new DateTime(2026, 4, 11, 0, 0, 0, DateTimeKind.Utc),
                "Commission", null, 25m, null, null, null, null,
                null, 1021, null, null, null)),
            CancellationToken.None);
        await new SubmitOtherIncomeCommandHandler(store).Handle(
            new SubmitOtherIncomeCommand(1, 101, rejectedId), CancellationToken.None);
        await new RejectOtherIncomeCommandHandler(store).Handle(
            new RejectOtherIncomeCommand(1, 101, rejectedId, "Missing deposit slip"), CancellationToken.None);

        var cancelledId = await new CreateOtherIncomeCommandHandler(store).Handle(
            new CreateOtherIncomeCommand(1, 101, new OtherIncomeCreationInput(
                new DateTime(2026, 4, 12, 0, 0, 0, DateTimeKind.Utc),
                "Miscellaneous", null, 20m, null, null, null, null,
                null, 1021, null, null, null)),
            CancellationToken.None);
        await new CancelOtherIncomeCommandHandler(store).Handle(
            new CancelOtherIncomeCommand(1, 101, cancelledId, "Created by mistake"), CancellationToken.None);

        Assert.Equal(OtherIncomeStatuses.Rejected, (await _db.OtherIncomeEntries.SingleAsync(x => x.Id == rejectedId)).Status);
        Assert.Equal(OtherIncomeStatuses.Cancelled, (await _db.OtherIncomeEntries.SingleAsync(x => x.Id == cancelledId)).Status);
        Assert.Null((await _db.OtherIncomeEntries.SingleAsync(x => x.Id == rejectedId)).JournalEntryId);
        Assert.Null((await _db.OtherIncomeEntries.SingleAsync(x => x.Id == cancelledId)).JournalEntryId);
        Assert.Equal(0, await _db.JournalEntries.CountAsync(x => x.ReferenceType == "OtherIncome"));
    }

    private OtherIncomeCommandStore CreateStore() => new(
        _db,
        _audit.Object,
        NullLogger<OtherIncomeCommandStore>.Instance,
        _journalPoster.Object,
        _accountResolver.Object);

    private void Seed()
    {
        _db.Businesses.Add(new Business { Id = 1, Name = "Other Income Business", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        _db.Outlets.Add(new Outlet { Id = 11, BusinessId = 1, Name = "Main Outlet", Address = "X", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });

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
            Account(6211, "6210", "Cash Difference Loss", ChartAccountTypes.Expense, NormalBalanceTypes.Debit)
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
            Id = 9201,
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
            Id = 9202,
            FinancialYearId = 9201,
            PeriodNumber = 4,
            Name = "April",
            StartDate = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2026, 4, 30, 0, 0, 0, DateTimeKind.Utc),
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
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }
}