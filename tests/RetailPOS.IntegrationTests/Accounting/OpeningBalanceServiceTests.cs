using Microsoft.EntityFrameworkCore;
using Moq;
using RetailPOS.Application.OpeningBalances.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.OpeningBalances;

namespace RetailPOS.Tests.Accounting;

public class OpeningBalanceCommandStoreTests : IDisposable
{
    private readonly RetailPOSDbContext _db;

    public OpeningBalanceCommandStoreTests()
    {
        var options = new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new RetailPOSDbContext(options);
        SeedBusinessAndAccounts();
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task PostAsync_Rejects_WhenMandatoryMappingsAreMissing()
    {
        var mappingValidator = CreateMappingValidator(["supplier_payable is required"]);
        var journalPoster = new Mock<IOpeningBalanceJournalPoster>();
        var store = CreateStore(mappingValidator, journalPoster.Object);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.PostAsync(1, 101, CreateOpeningBalanceInput(), CancellationToken.None));

        Assert.Contains("mandatory accounting configuration mappings", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("supplier_payable", ex.Message, StringComparison.OrdinalIgnoreCase);
        journalPoster.Verify(
            poster => poster.CreateOrGetAndPostAsync(
                It.IsAny<long>(),
                It.IsAny<long>(),
                It.IsAny<OpeningBalanceJournalInput>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PostAsync_Rejects_IncomeOrExpenseAccounts()
    {
        var input = CreateOpeningBalanceInput() with
        {
            Lines = CreateOpeningBalanceInput().Lines!
                .Append(new OpeningBalanceLineInput(4001, 0, 50, null, null, null))
                .ToArray()
        };
        var store = CreateStore(
            CreateMappingValidator(Array.Empty<string>()),
            Mock.Of<IOpeningBalanceJournalPoster>());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.PostAsync(1, 101, input, CancellationToken.None));

        Assert.Contains("Balance Sheet", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("4000", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PostAsync_Rejects_WhenOperationalJournalAlreadyPosted()
    {
        _db.JournalEntries.Add(new JournalEntry
        {
            Id = 9001,
            BusinessId = 1,
            JournalType = AccountingJournalTypes.ManualJournal,
            Status = JournalEntryStatuses.Posted,
            ReferenceType = "Manual",
            ReferenceId = "M-9001",
            PostingDate = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            TransactionDate = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var journalPoster = new Mock<IOpeningBalanceJournalPoster>();
        var store = CreateStore(
            Mock.Of<IOpeningBalanceMappingValidator>(),
            journalPoster.Object);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.PostAsync(1, 101, CreateOpeningBalanceInput(), CancellationToken.None));

        Assert.Contains("locked", ex.Message, StringComparison.OrdinalIgnoreCase);
        journalPoster.Verify(
            poster => poster.CreateOrGetAndPostAsync(
                It.IsAny<long>(),
                It.IsAny<long>(),
                It.IsAny<OpeningBalanceJournalInput>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PostAdjustmentCommand_Rejects_WhenPeriodIsLocked()
    {
        _db.JournalEntries.Add(new JournalEntry
        {
            Id = 9100,
            BusinessId = 1,
            JournalType = AccountingJournalTypes.OpeningBalance,
            Status = JournalEntryStatuses.Posted,
            ReferenceType = AccountingReferenceTypes.OpeningBalance,
            ReferenceId = "1",
            PostingDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            TransactionDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        _db.FinancialYears.Add(new FinancialYear
        {
            Id = 9300,
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
            Id = 9400,
            FinancialYearId = 9300,
            PeriodNumber = 2,
            Name = "Feb",
            StartDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2026, 2, 28, 0, 0, 0, DateTimeKind.Utc),
            Status = AccountingPeriodStatuses.Locked,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        var journalPoster = new Mock<IOpeningBalanceJournalPoster>();
        var store = new OpeningBalanceCommandStore(
            _db,
            Mock.Of<IOpeningBalanceMappingValidator>(),
            journalPoster.Object);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.PostAdjustmentAsync(
                1,
                101,
                9100,
                new OpeningBalanceAdjustmentInput(
                    "Correction",
                    new DateTime(2026, 2, 10, 0, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 2, 10, 0, 0, 0, DateTimeKind.Utc),
                    null,
                    null,
                    [
                        new OpeningBalanceLineInput(1001, 20, 0, null, null, null),
                        new OpeningBalanceLineInput(2001, 0, 20, null, null, null)
                    ]),
                CancellationToken.None));

        Assert.Contains("closed or locked", ex.Message, StringComparison.OrdinalIgnoreCase);
        journalPoster.Verify(
            poster => poster.CreateOrGetAndPostAsync(
                It.IsAny<long>(),
                It.IsAny<long>(),
                It.IsAny<OpeningBalanceJournalInput>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private OpeningBalanceCommandStore CreateStore(
        IOpeningBalanceMappingValidator mappingValidator,
        IOpeningBalanceJournalPoster journalPoster)
    {
        return new OpeningBalanceCommandStore(_db, mappingValidator, journalPoster);
    }

    private static IOpeningBalanceMappingValidator CreateMappingValidator(
        IReadOnlyList<string> errors)
    {
        var mock = new Mock<IOpeningBalanceMappingValidator>();
        mock.Setup(validator => validator.ValidateBalanceSheetMappingsAsync(
                It.IsAny<long>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(errors);
        return mock.Object;
    }

    private static OpeningBalanceInput CreateOpeningBalanceInput()
    {
        return new OpeningBalanceInput(
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            null,
            null,
            [
                new OpeningBalanceLineInput(1001, 1000, 0, null, null, null),
                new OpeningBalanceLineInput(2001, 0, 1000, null, null, null)
            ]);
    }

    private void SeedBusinessAndAccounts()
    {
        _db.Businesses.Add(new Business
        {
            Id = 1,
            Name = "Opening Balance Test Business",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        _db.ChartAccounts.AddRange(
            Account(1001, "1000", "Cash", ChartAccountTypes.Asset, NormalBalanceTypes.Debit),
            Account(1011, "1010", "Petty Cash", ChartAccountTypes.Asset, NormalBalanceTypes.Debit),
            Account(1021, "1020", "Bank", ChartAccountTypes.Asset, NormalBalanceTypes.Debit),
            Account(1101, "1100", "Inventory", ChartAccountTypes.Asset, NormalBalanceTypes.Debit),
            Account(2001, "2000", "Supplier Payable", ChartAccountTypes.Liability, NormalBalanceTypes.Credit),
            Account(2101, "2100", "Customer Advance", ChartAccountTypes.Liability, NormalBalanceTypes.Credit),
            Account(3001, "3000", "Owner Capital", ChartAccountTypes.Equity, NormalBalanceTypes.Credit),
            Account(3101, "3100", "Owner Drawings", ChartAccountTypes.Equity, NormalBalanceTypes.Debit),
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
            IsSystemAccount = true,
            IsControlAccount = false,
            AllowManualPosting = true,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }
}
