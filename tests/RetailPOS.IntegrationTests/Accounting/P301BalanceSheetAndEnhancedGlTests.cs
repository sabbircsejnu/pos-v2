using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RetailPOS.API.DTOs.Reports;
using RetailPOS.Application.AccountingReports.Abstractions;
using RetailPOS.Application.AccountingReports.Queries;
using RetailPOS.Application.AccountingReports.ReadModels;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.AccountingReports;

namespace RetailPOS.Tests.Accounting;

public class P301BalanceSheetAndEnhancedGlTests
{
    [Fact]
    public async Task BalanceSheet_UsesHierarchyNormalBalancesAndDynamicEarnings_AndReconciles()
    {
        await using var db = CreateDb();
        await SeedAsync(db);

        var report = await GetBalanceSheetQueryAsync(db, 1, new BalanceSheetReportFilter(new DateTime(2026, 1, 31), null, null));
        var cashLedger = await GetEnhancedGeneralLedgerQueryAsync(db, 1, new EnhancedGeneralLedgerFilterDto { AccountId = 2, DateFrom = new DateTime(2025, 1, 1), DateTo = new DateTime(2026, 1, 31) });
        var assetGroupLedger = await GetEnhancedGeneralLedgerQueryAsync(db, 1, new EnhancedGeneralLedgerFilterDto { AccountId = 1, DateFrom = new DateTime(2025, 1, 1), DateTo = new DateTime(2026, 1, 31) });

        Assert.Equal(215m, report.TotalAssets);
        Assert.Equal(10m, report.Liabilities.Total);
        Assert.Equal(205m, report.Equity.Total);
        Assert.Equal(20m, report.CurrentYearEarnings);
        Assert.Equal(40m, report.UnclosedPriorYearEarnings);
        Assert.Equal(0m, report.ReconciliationDifference);
        Assert.Null(report.CutoverWarning);
        var rootAsset = Assert.Single(report.Assets.Accounts);
        Assert.Equal(215m, rootAsset.RollupStatementBalance);
        Assert.Equal(cashLedger.ClosingBalance, rootAsset.Children.Single().RollupNormalBalance);
        Assert.Equal(assetGroupLedger.ClosingBalance, rootAsset.RollupNormalBalance);
        Assert.Equal(100m, report.Equity.Accounts.Single(x => x.PresentationRole == "OwnerCapital").StatementBalance);
        Assert.Equal(-5m, report.Equity.Accounts.Single(x => x.PresentationRole == "OwnerDrawings").StatementBalance);
        Assert.Equal(50m, report.Equity.Accounts.Single(x => x.PresentationRole == "RetainedEarnings").StatementBalance);
    }

    [Fact]
    public async Task BalanceSheet_ExcludesClosedYearDynamicEarningsWhenYearEndTransferExists()
    {
        await using var db = CreateDb();
        await SeedAsync(db);
        var yearEndClosing = Journal(10, new DateTime(2025, 12, 31), "JR-010", AccountingJournalTypes.YearEndClosing, "2025", (7, 40, 0), (6, 0, 40));
        yearEndClosing.ReferenceType = AccountingReferenceTypes.FinancialYearClose;
        db.JournalEntries.Add(yearEndClosing);
        await db.SaveChangesAsync();

        var report = await GetBalanceSheetQueryAsync(db, 1, new BalanceSheetReportFilter(new DateTime(2026, 1, 31), null, null));

        Assert.Equal(0m, report.UnclosedPriorYearEarnings);
        Assert.Equal(90m, report.Equity.Accounts.Single(x => x.PresentationRole == "RetainedEarnings").StatementBalance);
        Assert.Equal(0m, report.ReconciliationDifference);
    }

    [Fact]
    public async Task BalanceSheet_ExcludesUnpostedJournalsAndReconcilesAssetLiabilityAndEquityAccountsToEnhancedGl()
    {
        await using var db = CreateDb();
        await SeedAsync(db);
        db.JournalEntries.AddRange(
            Journal(30, new DateTime(2026, 1, 8), "JR-030", "ManualJournal", "30", (2, 999m, 0m), (4, 0m, 999m), status: JournalEntryStatuses.Draft),
            Journal(31, new DateTime(2026, 1, 8), "JR-031", "ManualJournal", "31", (2, 999m, 0m), (4, 0m, 999m), status: JournalEntryStatuses.Submitted),
            Journal(32, new DateTime(2026, 1, 8), "JR-032", "ManualJournal", "32", (2, 999m, 0m), (4, 0m, 999m), status: JournalEntryStatuses.Approved));
        await db.SaveChangesAsync();
        var balanceSheet = await GetBalanceSheetQueryAsync(db, 1, new BalanceSheetReportFilter(new DateTime(2026, 1, 31), null, null));

        Assert.Equal(215m, balanceSheet.TotalAssets);
        Assert.Equal(0m, balanceSheet.ReconciliationDifference);
        foreach (var accountId in new[] { 2L, 3L, 4L, 5L, 6L })
        {
            var ledger = await GetEnhancedGeneralLedgerQueryAsync(db, 1, new EnhancedGeneralLedgerFilterDto { AccountId = accountId, DateFrom = new DateTime(2025, 1, 1), DateTo = new DateTime(2026, 1, 31) });
            var balanceSheetAccount = FindAccount(balanceSheet, accountId);
            Assert.Equal(ledger.ClosingBalance, balanceSheetAccount.RollupNormalBalance);
        }
        Assert.DoesNotContain((await GetEnhancedGeneralLedgerQueryAsync(db, 1, new EnhancedGeneralLedgerFilterDto { AccountId = 2, DateFrom = new DateTime(2026, 1, 1), DateTo = new DateTime(2026, 1, 31) })).Rows, x => x.JournalEntryId is 30 or 31 or 32);
    }

    [Fact]
    public async Task EnhancedGeneralLedger_CalculatesOpeningMovementsRunningAndClosingInDeterministicOrder()
    {
        await using var db = CreateDb();
        await SeedAsync(db);
        var sourceJournal = await db.JournalEntries.Include(x => x.Lines).SingleAsync(x => x.Id == 4);
        sourceJournal.ReferenceNumber = "SALE-004";
        sourceJournal.Description = "January sale";
        sourceJournal.OutletId = 11;
        var sourceLine = sourceJournal.Lines.Single(x => x.AccountId == 2);
        sourceLine.Description = "Cash sale receipt";
        sourceLine.OutletId = 11;
        sourceLine.CustomerId = 71;
        sourceLine.PaymentMethodId = "cash";
        sourceLine.CostCenterId = "POS";
        await db.SaveChangesAsync();

        var report = await GetEnhancedGeneralLedgerQueryAsync(db, 1, new EnhancedGeneralLedgerFilterDto
        {
            AccountId = 2, DateFrom = new DateTime(2026, 1, 1), DateTo = new DateTime(2026, 1, 31)
        });

        Assert.Equal(190m, report.OpeningBalance);
        Assert.Equal(50m, report.TotalDebits);
        Assert.Equal(25m, report.TotalCredits);
        Assert.Equal(215m, report.ClosingBalance);
        Assert.Equal(new[] { "JR-004", "JR-005", "JR-006", "JR-007", "JR-008", "JR-009" }, report.Rows.Select(x => x.JournalNumber).ToArray());
        Assert.Equal(215m, report.Rows[3].RunningBalance);
        Assert.Equal(205m, report.Rows[4].RunningBalance);
        Assert.Equal(215m, report.Rows[^1].RunningBalance);
        Assert.Equal(7L, report.Rows[3].JournalEntryId);
        Assert.Equal(7L, report.Rows[4].ReversalOfJournalEntryId);
        Assert.Equal("Cash sale receipt", report.Rows[0].Narration);
        Assert.Equal("SALE-004", report.Rows[0].ReferenceNumber);
        Assert.Equal(11L, report.Rows[0].OutletId);
        Assert.Equal(71L, report.Rows[0].CustomerId);
        Assert.Equal("cash", report.Rows[0].PaymentMethodId);
        Assert.Equal("POS", report.Rows[0].CostCenterId);
    }

    [Fact]
    public async Task EnhancedGeneralLedger_OrdersSameDateByJournalNumberThenLineId_AndKeepsReversalInItsOwnPeriod()
    {
        await using var db = CreateDb();
        await SeedAsync(db);
        var original = await db.JournalEntries.SingleAsync(x => x.Id == 7);
        original.PostingDate = new DateTime(2026, 1, 15);
        var reversal = await db.JournalEntries.SingleAsync(x => x.Id == 8);
        reversal.PostingDate = new DateTime(2026, 2, 5);
        db.JournalEntries.Add(new JournalEntry
        {
            Id = 40, BusinessId = 1, JournalNumber = "JR-010", JournalType = "ManualJournal", ReferenceType = "ManualJournal", ReferenceId = "40", PostingDate = new DateTime(2026, 2, 1), Status = JournalEntryStatuses.Posted,
            Lines = [new JournalEntryLine { Id = 901, AccountId = 2, DebitAmount = 2m }, new JournalEntryLine { Id = 900, AccountId = 2, DebitAmount = 1m }, new JournalEntryLine { Id = 902, AccountId = 3, CreditAmount = 3m }]
        });
        db.JournalEntries.Add(Journal(41, new DateTime(2026, 2, 1), "JR-002", "ManualJournal", "41", (2, 4m, 0m), (3, 0m, 4m)));
        await db.SaveChangesAsync();

        var january = await GetEnhancedGeneralLedgerQueryAsync(db, 1, new EnhancedGeneralLedgerFilterDto { AccountId = 2, DateFrom = new DateTime(2026, 1, 1), DateTo = new DateTime(2026, 1, 31) });
        var february = await GetEnhancedGeneralLedgerQueryAsync(db, 1, new EnhancedGeneralLedgerFilterDto { AccountId = 2, DateFrom = new DateTime(2026, 2, 1), DateTo = new DateTime(2026, 2, 28) });

        Assert.Contains(january.Rows, x => x.JournalEntryId == 7);
        Assert.DoesNotContain(january.Rows, x => x.JournalEntryId == 8);
        Assert.Contains(february.Rows, x => x.JournalEntryId == 8 && x.ReversalOfJournalEntryId == 7);
        Assert.Equal(new[] { 41L, 40L, 40L, 8L }, february.Rows.Select(x => x.JournalEntryId).ToArray());
        Assert.Equal(new[] { 900L, 901L }, february.Rows.Where(x => x.JournalEntryId == 40).Select(x => x.JournalEntryLineId).ToArray());
    }

    [Fact]
    public async Task ComparativeBalanceSheetAndProfitLoss_AreCalculatedIndependently_AndCutoverIsVisible()
    {
        await using var db = CreateDb();
        await SeedAsync(db);

        var comparativeBalanceSheet = await GetComparativeBalanceSheetQueryAsync(
            db,
            1,
            new BalanceSheetReportFilter(new DateTime(2026, 1, 31), null, null),
            new DateTime(2025, 12, 31));
        var comparativePnl = await GetComparativeProfitLossQueryAsync(
            db,
            1,
            new ComparativeProfitLossReportFilter(
                new DateTime(2026, 1, 1),
                new DateTime(2026, 1, 31),
                new DateTime(2025, 1, 1),
                new DateTime(2025, 12, 31),
                null,
                null));

        Assert.Equal(215m, comparativeBalanceSheet.Current.TotalAssets);
        Assert.Equal(190m, comparativeBalanceSheet.Comparison.TotalAssets);
        Assert.Equal(20m, comparativePnl.Current.NetProfit);
        Assert.Equal(40m, comparativePnl.Comparison.NetProfit);

        await using var emptyDb = CreateDb();
        await SeedAccountsAsync(emptyDb, 1);
        var cutover = await GetBalanceSheetQueryAsync(emptyDb, 1, new BalanceSheetReportFilter(new DateTime(2026, 1, 31), null, null));
        Assert.NotNull(cutover.CutoverWarning);
    }

    [Fact]
    public async Task BalanceSheet_RejectsInvalidLocationType()
    {
        await using var db = CreateDb();
        await SeedAsync(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() => GetBalanceSheetQueryAsync(
            db,
            1,
            new BalanceSheetReportFilter(new DateTime(2026, 1, 31), 11, "invalid")));
    }

    [Fact]
    public async Task ComparativeProfitLoss_IncludesOriginalAndReversalInTheirOwnPostingPeriods()
    {
        await using var db = CreateDb();
        await SeedAsync(db);
        db.JournalEntries.AddRange(
            Journal(60, new DateTime(2026, 1, 15), "JR-060", "SalePosting", "60", (2, 10m, 0m), (7, 0m, 10m), status: JournalEntryStatuses.Reversed, reversedBy: 61),
            Journal(61, new DateTime(2026, 2, 5), "JR-061", AccountingJournalTypes.JournalReversal, "60", (7, 10m, 0m), (2, 0m, 10m), reversalOf: 60));
        await db.SaveChangesAsync();

        var report = await GetComparativeProfitLossQueryAsync(
            db,
            1,
            new ComparativeProfitLossReportFilter(
                new DateTime(2026, 2, 1),
                new DateTime(2026, 2, 28),
                new DateTime(2026, 1, 1),
                new DateTime(2026, 1, 31),
                null,
                null));

        Assert.Equal(-10m, report.Current.NetProfit);
        Assert.Equal(30m, report.Comparison.NetProfit);
    }

    [Fact]
    public async Task LocationScopedBalanceSheet_IdentifiesManagementViewAndSurfacesUnassignedCounterpartDifference()
    {
        await using var db = CreateDb();
        await SeedAsync(db);
        var scopedJournal = Journal(50, new DateTime(2026, 1, 8), "JR-050", "ManualJournal", "50", (2, 10m, 0m), (4, 0m, 10m));
        scopedJournal.Lines.First().OutletId = 11;
        db.JournalEntries.Add(scopedJournal);
        await db.SaveChangesAsync();

        var report = await GetBalanceSheetQueryAsync(
            db,
            1,
            new BalanceSheetReportFilter(new DateTime(2026, 1, 31), 11, "outlet"));

        Assert.True(report.IsManagementView);
        Assert.Equal(10m, report.ReconciliationDifference);
    }

    [Fact]
    public async Task BalanceSheetAndEnhancedLedger_AreTenantScoped()
    {
        await using var db = CreateDb();
        await SeedAsync(db);
        await SeedAccountsAsync(db, 2);
        db.JournalEntries.Add(Journal(20, new DateTime(2026, 1, 2), "JR-020", "ManualJournal", "20", (2, 999, 0), (4, 0, 999), businessId: 2));
        await db.SaveChangesAsync();

        var balanceSheet = await GetBalanceSheetQueryAsync(db, 1, new BalanceSheetReportFilter(new DateTime(2026, 1, 31), null, null));
        Assert.Equal(215m, balanceSheet.TotalAssets);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => GetEnhancedGeneralLedgerQueryAsync(db, 1, new EnhancedGeneralLedgerFilterDto { AccountId = 2002 }));
    }

    private static Task<BalanceSheetReportReadModel> GetBalanceSheetQueryAsync(
        RetailPOSDbContext db,
        long businessId,
        BalanceSheetReportFilter filter)
    {
        var tenantScope = new Mock<IAccountingReportTenantScope>();
        tenantScope.Setup(scope => scope.GetBusinessIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(businessId);
        var handler = new GetBalanceSheetQueryHandler(
            new BalanceSheetReadStore(db),
            tenantScope.Object);
        return handler.Handle(new GetBalanceSheetQuery(filter), CancellationToken.None);
    }

    private static Task<ComparativeBalanceSheetReadModel> GetComparativeBalanceSheetQueryAsync(
        RetailPOSDbContext db,
        long businessId,
        BalanceSheetReportFilter filter,
        DateTime? comparisonAsOfDate)
    {
        var tenantScope = new Mock<IAccountingReportTenantScope>();
        tenantScope.Setup(scope => scope.GetBusinessIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(businessId);
        var handler = new GetComparativeBalanceSheetQueryHandler(
            new BalanceSheetReadStore(db),
            tenantScope.Object);
        return handler.Handle(
            new GetComparativeBalanceSheetQuery(filter, comparisonAsOfDate),
            CancellationToken.None);
    }

    private static Task<ComparativeProfitLossReadModel> GetComparativeProfitLossQueryAsync(
        RetailPOSDbContext db,
        long businessId,
        ComparativeProfitLossReportFilter filter)
    {
        var tenantScope = new Mock<IAccountingReportTenantScope>();
        tenantScope.Setup(scope => scope.GetBusinessIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(businessId);
        var handler = new GetComparativeProfitLossQueryHandler(
            new GeneralLedgerReadStore(db),
            tenantScope.Object);
        return handler.Handle(
            new GetComparativeProfitLossQuery(filter),
            CancellationToken.None);
    }

    private static Task<EnhancedGeneralLedgerReadModel> GetEnhancedGeneralLedgerQueryAsync(
        RetailPOSDbContext db,
        long businessId,
        EnhancedGeneralLedgerFilterDto filter)
    {
        var tenantScope = new Mock<IAccountingReportTenantScope>();
        tenantScope.Setup(scope => scope.GetBusinessIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(businessId);
        var handler = new GetEnhancedGeneralLedgerQueryHandler(
            new EnhancedGeneralLedgerReadStore(db),
            tenantScope.Object);
        return handler.Handle(
            new GetEnhancedGeneralLedgerQuery(new EnhancedGeneralLedgerFilter(
                filter.AccountId,
                filter.DateFrom,
                filter.DateTo,
                filter.LocationId,
                filter.LocationType,
                filter.JournalType,
                filter.ReferenceType,
                filter.Reference)),
            CancellationToken.None);
    }

    private static BalanceSheetAccountDto FindAccount(BalanceSheetReportDto report, long accountId)
    {
        foreach (var section in new[] { report.Assets, report.Liabilities, report.Equity })
        {
            var account = FindAccount(section.Accounts, accountId);
            if (account != null) return account;
        }
        throw new InvalidOperationException($"Account {accountId} was not found.");
    }

    private static BalanceSheetAccountDto? FindAccount(IEnumerable<BalanceSheetAccountDto> accounts, long accountId)
    {
        foreach (var account in accounts)
        {
            if (account.AccountId == accountId) return account;
            var child = FindAccount(account.Children, accountId);
            if (child != null) return child;
        }
        return null;
    }

    private static BalanceSheetAccountReadModel FindAccount(
        BalanceSheetReportReadModel report,
        long accountId)
    {
        foreach (var section in new[] { report.Assets, report.Liabilities, report.Equity })
        {
            var account = FindAccount(section.Accounts, accountId);
            if (account is not null)
                return account;
        }

        throw new InvalidOperationException($"Account {accountId} was not found.");
    }

    private static BalanceSheetAccountReadModel? FindAccount(
        IEnumerable<BalanceSheetAccountReadModel> accounts,
        long accountId)
    {
        foreach (var account in accounts)
        {
            if (account.AccountId == accountId)
                return account;
            var child = FindAccount(account.Children, accountId);
            if (child is not null)
                return child;
        }

        return null;
    }

    private static async Task SeedAsync(RetailPOSDbContext db)
    {
        await SeedAccountsAsync(db, 1);
        db.FinancialYears.AddRange(
            new FinancialYear { Id = 2025, BusinessId = 1, YearCode = "FY25", Name = "FY25", StartDate = new DateTime(2025, 1, 1), EndDate = new DateTime(2025, 12, 31) },
            new FinancialYear { Id = 2026, BusinessId = 1, YearCode = "FY26", Name = "FY26", StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2026, 12, 31), IsCurrent = true });
        db.JournalEntries.AddRange(
            Journal(1, new DateTime(2025, 1, 1), "JR-001", "OpeningBalance", "1", (2, 100, 0), (4, 0, 100)),
            Journal(2, new DateTime(2025, 2, 1), "JR-002", "SalePosting", "2", (2, 40, 0), (7, 0, 40)),
            Journal(3, new DateTime(2025, 3, 1), "JR-003", "ManualJournal", "3", (2, 50, 0), (6, 0, 50)),
            Journal(4, new DateTime(2026, 1, 2), "JR-004", "SalePosting", "4", (2, 30, 0), (7, 0, 30)),
            Journal(5, new DateTime(2026, 1, 3), "JR-005", "ExpensePosting", "5", (9, 10, 0), (2, 0, 10)),
            Journal(6, new DateTime(2026, 1, 4), "JR-006", "OwnerDrawings", "6", (5, 5, 0), (2, 0, 5)),
            Journal(7, new DateTime(2026, 1, 5), "JR-007", "ManualJournal", "7", (2, 10, 0), (3, 0, 10), status: JournalEntryStatuses.Reversed, reversedBy: 8),
            Journal(8, new DateTime(2026, 1, 6), "JR-008", "JournalReversal", "7", (3, 10, 0), (2, 0, 10), reversalOf: 7),
            Journal(9, new DateTime(2026, 1, 7), "JR-009", "ManualJournal", "9", (2, 10, 0), (3, 0, 10)));
        await db.SaveChangesAsync();
    }

    private static async Task SeedAccountsAsync(RetailPOSDbContext db, long businessId)
    {
        db.Businesses.Add(new Business { Id = businessId, Name = $"Business {businessId}" });
        var offset = businessId == 1 ? 0 : 2000;
        db.ChartAccounts.AddRange(
            Account(offset + 1, businessId, "1000", "Current Assets", ChartAccountTypes.Asset, NormalBalanceTypes.Debit),
            Account(offset + 2, businessId, "1010", "Cash", ChartAccountTypes.Asset, NormalBalanceTypes.Debit, offset + 1),
            Account(offset + 3, businessId, "2000", "Payables", ChartAccountTypes.Liability, NormalBalanceTypes.Credit),
            Account(offset + 4, businessId, "3000", "Owner Capital", ChartAccountTypes.Equity, NormalBalanceTypes.Credit),
            Account(offset + 5, businessId, "3100", "Owner Drawings", ChartAccountTypes.Equity, NormalBalanceTypes.Debit),
            Account(offset + 6, businessId, "3200", "Retained Earnings", ChartAccountTypes.Equity, NormalBalanceTypes.Credit),
            Account(offset + 7, businessId, "4000", "Sales", ChartAccountTypes.Income, NormalBalanceTypes.Credit),
            Account(offset + 8, businessId, "5000", "COGS", ChartAccountTypes.CostOfGoodsSold, NormalBalanceTypes.Debit),
            Account(offset + 9, businessId, "5100", "Expenses", ChartAccountTypes.Expense, NormalBalanceTypes.Debit));
        db.AccountingConfigurations.Add(new AccountingConfiguration { BusinessId = businessId, OwnerCapitalAccountId = offset + 4, OwnerDrawingsAccountId = offset + 5, RetainedEarningsAccountId = offset + 6, JournalNumberPrefix = "JR", JournalSequenceResetPolicy = AccountingSequenceResetPolicies.PerFinancialYear });
        await db.SaveChangesAsync();
    }

    private static ChartAccount Account(long id, long businessId, string code, string name, string type, string normal, long? parentId = null) => new()
    {
        Id = id, BusinessId = businessId, AccountCode = code, AccountName = name, AccountType = type, NormalBalanceType = normal, ParentAccountId = parentId, IsActive = true
    };

    private static JournalEntry Journal(long id, DateTime date, string number, string type, string referenceId, params (long AccountId, decimal Debit, decimal Credit)[] lines)
        => Journal(id, date, number, type, referenceId, lines[0], lines[1]);

    private static JournalEntry Journal(long id, DateTime date, string number, string type, string referenceId, (long AccountId, decimal Debit, decimal Credit) first, (long AccountId, decimal Debit, decimal Credit) second, string status = JournalEntryStatuses.Posted, long? reversedBy = null, long? reversalOf = null, long businessId = 1) => new()
    {
        Id = id, BusinessId = businessId, JournalNumber = number, JournalType = type, ReferenceType = type, ReferenceId = referenceId, PostingDate = date, BusinessDate = date,
        Status = status, ReversedByJournalEntryId = reversedBy, ReversalOfJournalEntryId = reversalOf,
        Lines = [new JournalEntryLine { AccountId = first.AccountId, DebitAmount = first.Debit, CreditAmount = first.Credit }, new JournalEntryLine { AccountId = second.AccountId, DebitAmount = second.Debit, CreditAmount = second.Credit }]
    };

    private static RetailPOSDbContext CreateDb() => new(new DbContextOptionsBuilder<RetailPOSDbContext>().UseInMemoryDatabase($"P301_{Guid.NewGuid()}").Options);
}