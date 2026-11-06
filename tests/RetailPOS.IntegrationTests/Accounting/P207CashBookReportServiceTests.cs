using Microsoft.EntityFrameworkCore;
using Moq;
using RetailPOS.API.DTOs.Accounting;
using RetailPOS.Application.AccountingReports.Abstractions;
using RetailPOS.Application.AccountingReports.Queries;
using RetailPOS.Application.AccountingReports.ReadModels;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.AccountingReports;
using RetailPOS.Infrastructure.Data;

using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Accounting;

public class P207CashBookReportServiceTests
{
    [Fact]
    public async Task CashBookQuery_UsesResolvedAccountAndPreservesHistoricalBalancesAndReversalRows()
    {
        await using var db = CreateDb();
        await SeedCashBookAsync(db);
        var tenantScope = new Mock<IAccountingReportTenantScope>();
        tenantScope.Setup(scope => scope.GetBusinessIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1L);
        var resolver = new Mock<ICashBookPostingAccountResolver>();
        resolver.Setup(accountResolver => accountResolver.ResolveAsync(
                PostingAccountSlots.Cash,
                11,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CashBookAccountResolution(PostingAccountSlots.Cash, 1));
        var handler = new GetCashBookQueryHandler(
            new CashBookReadStore(db),
            resolver.Object,
            tenantScope.Object,
            TestCompanySettings.Provider());

        var report = await handler.Handle(
            new GetCashBookQuery(
                new CashBookFilter(
                    new DateTime(2026, 1, 2),
                    new DateTime(2026, 1, 31),
                    11,
                    "outlet",
                    null,
                    null,
                    null),
                [PostingAccountSlots.Cash]),
            CancellationToken.None);

        var cash = Assert.Single(report.Accounts);
        Assert.Equal(100m, cash.OpeningBalance);
        Assert.Equal(38m, cash.TotalInflows);
        Assert.Equal(37m, cash.TotalOutflows);
        Assert.Equal(101m, cash.ClosingBalance);
        Assert.Equal(101m, cash.GeneralLedgerBalance);
        Assert.Equal(0m, cash.ReconciliationDifference);
        Assert.Equal(10, cash.Rows.Count);
        Assert.Contains(cash.Rows, row => row.ReversalOfJournalEntryId == 10);
        Assert.Contains(cash.Rows, row => row.ReversedByJournalEntryId == 11);
        resolver.Verify(accountResolver => accountResolver.ResolveAsync(
            PostingAccountSlots.Cash,
            11,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CashBook_UsesOnlyJournalLines_ForOpeningInflowsOutflowsAndRunningBalance()
    {
        await using var db = CreateDb();
        await SeedCashBookAsync(db);

        var report = await GetCashBookQueryAsync(
            db,
            new DateTime(2026, 1, 2),
            new DateTime(2026, 1, 31),
            null,
            null,
            [PostingAccountSlots.Cash],
            new Dictionary<string, long> { [PostingAccountSlots.Cash] = 1 });
        var cash = Assert.Single(report.Accounts);

        Assert.Equal(100m, cash.OpeningBalance);
        Assert.Equal(38m, cash.TotalInflows);
        Assert.Equal(37m, cash.TotalOutflows);
        Assert.Equal(101m, cash.ClosingBalance);
        Assert.Equal(101m, cash.GeneralLedgerBalance);
        Assert.Equal(0m, cash.ReconciliationDifference);
        Assert.Equal(10, cash.Rows.Count);
        Assert.Equal(120m, cash.Rows[0].RunningBalance);
        Assert.Contains(cash.Rows, row => row.JournalType == "CashDifferenceSurplusPosting");
        Assert.Contains(cash.Rows, row => row.JournalType == "SupplierAdvancePosting");
        Assert.All(cash.Rows, row => Assert.NotEmpty(row.CounterpartAccounts));
    }

    [Fact]
    public async Task CashBook_SeparatesConfiguredCashAndBank_AndFiltersTenantAndLocation()
    {
        await using var db = CreateDb();
        await SeedCashBookAsync(db);
        db.JournalEntries.Add(new JournalEntry
        {
            Id = 99, BusinessId = 2, Status = JournalEntryStatuses.Posted, PostingDate = new DateTime(2026, 1, 2),
            Lines = [new JournalEntryLine { AccountId = 1, DebitAmount = 999m }, new JournalEntryLine { AccountId = 4, CreditAmount = 999m }]
        });
        db.JournalEntries.Add(new JournalEntry
        {
            Id = 98, BusinessId = 1, Status = JournalEntryStatuses.Posted, PostingDate = new DateTime(2026, 1, 2), OutletId = 12,
            Lines = [new JournalEntryLine { AccountId = 1, DebitAmount = 250m, OutletId = 12 }, new JournalEntryLine { AccountId = 4, CreditAmount = 250m, OutletId = 12 }]
        });
        await db.SaveChangesAsync();

        var report = await GetCashBookQueryAsync(
            db,
            new DateTime(2026, 1, 1),
            new DateTime(2026, 1, 31),
            11,
            "outlet",
            [PostingAccountSlots.Cash, PostingAccountSlots.Bank],
            new Dictionary<string, long>
            {
                [PostingAccountSlots.Cash] = 1,
                [PostingAccountSlots.Bank] = 3
            });

        Assert.Equal(2, report.Accounts.Count);
        Assert.Equal(101m, report.Accounts.Single(x => x.AccountId == 1).ClosingBalance);
        Assert.Equal(50m, report.Accounts.Single(x => x.AccountId == 3).ClosingBalance);
    }

    [Fact]
    public async Task CashBook_UsesOutletResolvedCashAccount()
    {
        await using var db = CreateDb();
        await SeedCashBookAsync(db);
        var report = await GetCashBookQueryAsync(
            db,
            new DateTime(2026, 1, 1),
            new DateTime(2026, 1, 31),
            11,
            "outlet",
            [PostingAccountSlots.Cash],
            new Dictionary<string, long> { [PostingAccountSlots.Cash] = 3 });

        Assert.Equal(3, Assert.Single(report.Accounts).AccountId);
    }

    [Fact]
    public async Task CashBook_ReportsReversedOriginalInOriginalPeriod_AndPostedReversalInItsOwnPeriod()
    {
        await using var db = CreateDb();
        await SeedCashBookAsync(db);

        var accountSlots = new[] { PostingAccountSlots.Cash };
        var accountIds = new Dictionary<string, long> { [PostingAccountSlots.Cash] = 1 };
        var originalPeriod = await GetCashBookQueryAsync(
            db,
            new DateTime(2026, 1, 10),
            new DateTime(2026, 1, 10),
            null,
            null,
            accountSlots,
            accountIds);
        var reversalPeriod = await GetCashBookQueryAsync(
            db,
            new DateTime(2026, 1, 11),
            new DateTime(2026, 1, 11),
            null,
            null,
            accountSlots,
            accountIds);

        Assert.Equal(9m, Assert.Single(originalPeriod.Accounts).TotalInflows);
        Assert.Equal(9m, Assert.Single(reversalPeriod.Accounts).TotalOutflows);
    }

    private static Task<CashBookReadModel> GetCashBookQueryAsync(
        RetailPOSDbContext db,
        DateTime dateFrom,
        DateTime dateTo,
        long? locationId,
        string? locationType,
        IReadOnlyList<string> accountSlots,
        IReadOnlyDictionary<string, long> accountIds)
    {
        var tenantScope = new Mock<IAccountingReportTenantScope>();
        tenantScope.Setup(scope => scope.GetBusinessIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1L);
        var resolver = new Mock<ICashBookPostingAccountResolver>();
        resolver.Setup(accountResolver => accountResolver.ResolveAsync(
                It.IsAny<string>(),
                It.IsAny<long?>(),
                It.IsAny<CancellationToken>()))
            .Returns((string slot, long? _, CancellationToken _) =>
                Task.FromResult(new CashBookAccountResolution(slot, accountIds[slot])));
        var handler = new GetCashBookQueryHandler(
            new CashBookReadStore(db),
            resolver.Object,
            tenantScope.Object,
            TestCompanySettings.Provider());
        return handler.Handle(
            new GetCashBookQuery(
                new CashBookFilter(
                    dateFrom,
                    dateTo,
                    locationId,
                    locationType,
                    null,
                    null,
                    null),
                accountSlots),
            CancellationToken.None);
    }

    private static async Task SeedCashBookAsync(RetailPOSDbContext db)
    {
        db.Businesses.AddRange(new Business { Id = 1, Name = "Cash Book" }, new Business { Id = 2, Name = "Other" });
        db.Outlets.Add(new Outlet { Id = 11, BusinessId = 1, Name = "Outlet", Address = "A" });
        db.ChartAccounts.AddRange(Account(1, "Cash"), Account(2, "Petty Cash"), Account(3, "Bank"), Account(4, "Counterpart"));
        db.JournalEntries.AddRange(
            Journal(1, new DateTime(2026, 1, 1), "OpeningBalance", 1, 100, 0),
            Journal(2, new DateTime(2026, 1, 2), "SalePosting", 1, 20, 0),
            Journal(3, new DateTime(2026, 1, 3), "SalesReturnPosting", 1, 0, 5),
            Journal(4, new DateTime(2026, 1, 4), "ExpensePosting", 1, 0, 10),
            Journal(5, new DateTime(2026, 1, 5), "OtherIncomePosting", 1, 7, 0),
            Journal(6, new DateTime(2026, 1, 6), "SupplierPaymentPosting", 1, 0, 8),
            Journal(7, new DateTime(2026, 1, 7), "SupplierAdvancePosting", 1, 0, 4),
            Journal(8, new DateTime(2026, 1, 8), "CashDifferenceSurplusPosting", 1, 2, 0),
            Journal(9, new DateTime(2026, 1, 9), "ManualJournal", 1, 0, 1),
            Journal(10, new DateTime(2026, 1, 10), "ManualJournal", 1, 9, 0, JournalEntryStatuses.Reversed, 11),
            Journal(11, new DateTime(2026, 1, 11), "ManualJournalReversal", 1, 0, 9, JournalEntryStatuses.Posted, reversalOf: 10),
            Journal(12, new DateTime(2026, 1, 2), "SalePosting", 3, 50, 0));
        await db.SaveChangesAsync();
    }

    private static ChartAccount Account(long id, string name) => new()
    {
        Id = id, BusinessId = 1, AccountCode = id.ToString("0000"), AccountName = name,
        AccountType = ChartAccountTypes.Asset, NormalBalanceType = NormalBalanceTypes.Debit, IsActive = true
    };

    private static JournalEntry Journal(long id, DateTime date, string type, long accountId, decimal debit, decimal credit, string status = JournalEntryStatuses.Posted, long? reversedBy = null, long? reversalOf = null) => new()
    {
        Id = id, BusinessId = 1, JournalNumber = $"JR-{id:D3}", JournalType = type, ReferenceType = type, ReferenceId = id.ToString(),
        PostingDate = date, BusinessDate = date, Status = status, OutletId = 11, ReversedByJournalEntryId = reversedBy, ReversalOfJournalEntryId = reversalOf,
        Lines = [new JournalEntryLine { AccountId = accountId, DebitAmount = debit, CreditAmount = credit, OutletId = 11, Description = type }, new JournalEntryLine { AccountId = 4, DebitAmount = credit, CreditAmount = debit, OutletId = 11 }]
    };

    private static RetailPOSDbContext CreateDb() => new(new DbContextOptionsBuilder<RetailPOSDbContext>().UseInMemoryDatabase($"P207_{Guid.NewGuid()}").Options);
}