using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MediatR;
using Moq;
using RetailPOS.API.DTOs.Accounting;
using RetailPOS.API.DTOs.Reports;
using RetailPOS.Application;
using RetailPOS.Application.AccountingReports.Abstractions;
using RetailPOS.Application.AccountingReports.Queries;
using RetailPOS.Application.AccountingReports.ReadModels;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.AccountingReports;
using RetailPOS.Infrastructure.Data;

using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Accounting;

public class P308CashFlowReportServiceTests
{
    [Fact]
    public async Task CashFlow_ClassifiesExternalLiquidity_AndReconcilesToCashBookGlMovement()
    {
        await using var db = CreateDb();
        await SeedCoreAsync(db);
        db.JournalEntries.AddRange(
            Journal(1, new DateTime(2026, 1, 1), AccountingJournalTypes.OpeningBalance, AccountingReferenceTypes.OpeningBalance, 1, 100, 0),
            Journal(2, new DateTime(2026, 1, 2), "SalePosting", "Sale", 1, 50, 0),
            Journal(3, new DateTime(2026, 1, 3), "SupplierPaymentPosting", "SupplierPayment", 1, 0, 20),
            Journal(4, new DateTime(2026, 1, 4), "ExpensePosting", "Expense", 2, 0, 10),
            Journal(5, new DateTime(2026, 1, 5), "OtherIncomePosting", "OtherIncome", 3, 7, 0),
            Journal(6, new DateTime(2026, 1, 6), "BankSettlement", "SettlementBatch", 3, 90, 0, counterpartAccountId: 7),
            Journal(7, new DateTime(2026, 1, 7), "LiquidityTransfer", "LiquidityTransfer", 1, 0, 30, secondLiquidityAccountId: 3, secondLiquidityDebit: 30),
            Journal(8, new DateTime(2026, 1, 8), "ManualJournal", "OwnerCapital", 1, 40, 0, counterpartAccountId: 6),
            Journal(9, new DateTime(2026, 1, 9), "FixedAssetPurchasePosting", "FixedAssetPurchase", 3, 0, 15),
            Journal(10, new DateTime(2026, 1, 10), "ManualJournal", "ManualJournal", 1, 3, 0));
        await db.SaveChangesAsync();

        var report = await GetCashFlowAsync(db, new CashFlowReportFilterDto
        {
            DateFrom = new DateTime(2026, 1, 2),
            DateTo = new DateTime(2026, 1, 31)
        });

        Assert.Equal(100m, report.OpeningLiquidity);
        Assert.Equal(245m, report.ClosingLiquidity);
        Assert.Equal(117m, report.OperatingNetMovement);
        Assert.Equal(-15m, report.InvestingNetMovement);
        Assert.Equal(40m, report.FinancingNetMovement);
        Assert.Equal(3m, report.UnclassifiedNetMovement);
        Assert.Equal(145m, report.TotalExternalNetMovement);
        Assert.Equal(145m, report.GlDerivedMovement);
        Assert.Equal(0m, report.ReconciliationDifference);
        Assert.Equal(0m, report.InternalTransferNetMovement);
        Assert.Contains(report.InternalTransferRows, row => row.ReferenceType == "LiquidityTransfer");
        Assert.Contains(report.Sections.Single(x => x.ActivityType == "Unclassified").Rows, row => row.ReferenceId == "10");
    }

    [Theory]
    [InlineData("cash_to_bank", 1, 0, 25, 3, 25, 0)]
    [InlineData("bank_to_cash", 3, 0, 25, 1, 25, 0)]
    [InlineData("bank_to_bank", 3, 0, 25, 3, 25, 0)]
    public async Task CashFlow_EliminatesInternalTransfers_ByAuthoritativeReference(string transferType, long sourceAccountId, decimal sourceDebit, decimal sourceCredit, long destinationAccountId, decimal destinationDebit, decimal destinationCredit)
    {
        await using var db = CreateDb();
        await SeedCoreAsync(db);
        db.JournalEntries.Add(Journal(20, new DateTime(2026, 2, 1), "LiquidityTransfer", "LiquidityTransfer", sourceAccountId, sourceDebit, sourceCredit, description: transferType, secondLiquidityAccountId: destinationAccountId, secondLiquidityDebit: destinationDebit, secondLiquidityCredit: destinationCredit));
        await db.SaveChangesAsync();

        var report = await GetCashFlowAsync(db, new CashFlowReportFilterDto { DateFrom = new DateTime(2026, 2, 1), DateTo = new DateTime(2026, 2, 28) });

        Assert.Equal(0m, report.TotalExternalNetMovement);
        Assert.Equal(0m, report.InternalTransferNetMovement);
        Assert.Equal(2, report.InternalTransferRows.Count);
        Assert.DoesNotContain(report.Sections.SelectMany(x => x.Rows), x => x.ReferenceType == "LiquidityTransfer");
    }

    [Fact]
    public async Task CashFlow_TreatsCardClearingAsNonLiquidity_AndCountsSettlementBankMovementOnce()
    {
        await using var db = CreateDb();
        await SeedCoreAsync(db);
        db.JournalEntries.AddRange(
            Journal(30, new DateTime(2026, 3, 1), "SalePosting", "Sale", 7, 70, 0, counterpartAccountId: 5),
            Journal(31, new DateTime(2026, 3, 2), "BankSettlement", "SettlementBatch", 3, 70, 0, counterpartAccountId: 7));
        await db.SaveChangesAsync();

        var report = await GetCashFlowAsync(db, new CashFlowReportFilterDto { DateFrom = new DateTime(2026, 3, 1), DateTo = new DateTime(2026, 3, 31) });
        var rows = report.Sections.SelectMany(x => x.Rows).ToList();

        Assert.Equal(70m, report.OperatingNetMovement);
        Assert.Single(rows);
        Assert.Equal(3, rows[0].AccountId);
        Assert.DoesNotContain(rows, row => row.AccountId == 7);
    }

    [Fact]
    public async Task CashFlow_ExcludesCutoverAndYearEndClosing_ButSurfacesReconciliationDifference()
    {
        await using var db = CreateDb();
        await SeedCoreAsync(db);
        db.JournalEntries.AddRange(
            Journal(40, new DateTime(2026, 4, 1), AccountingJournalTypes.OpeningBalance, AccountingReferenceTypes.OpeningBalance, 1, 25, 0),
            Journal(41, new DateTime(2026, 4, 2), AccountingJournalTypes.YearEndClosing, AccountingReferenceTypes.FinancialYearClose, 1, 0, 5));
        await db.SaveChangesAsync();

        var report = await GetCashFlowAsync(db, new CashFlowReportFilterDto { DateFrom = new DateTime(2026, 4, 1), DateTo = new DateTime(2026, 4, 30) });

        Assert.Equal(0m, report.TotalExternalNetMovement);
        Assert.Equal(20m, report.GlDerivedMovement);
        Assert.Equal(-20m, report.ReconciliationDifference);
        Assert.Equal(20m, report.CutoverAndNonCashNetMovement);
        Assert.Equal(2, report.CutoverAndNonCashRows.Count);
    }

    [Fact]
    public async Task CashFlow_ReportsReversalInPostingPeriod_WithOriginalEconomicClassification()
    {
        await using var db = CreateDb();
        await SeedCoreAsync(db);
        db.JournalEntries.AddRange(
            Journal(50, new DateTime(2026, 5, 10), "SalePosting", "Sale", 1, 9, 0, JournalEntryStatuses.Reversed, reversedBy: 51),
            Journal(51, new DateTime(2026, 6, 1), AccountingJournalTypes.JournalReversal, "Sale", 1, 0, 9, reversalOf: 50));
        await db.SaveChangesAsync();

        var originalPeriod = await GetCashFlowAsync(db, new CashFlowReportFilterDto { DateFrom = new DateTime(2026, 5, 1), DateTo = new DateTime(2026, 5, 31) });
        var reversalPeriod = await GetCashFlowAsync(db, new CashFlowReportFilterDto { DateFrom = new DateTime(2026, 6, 1), DateTo = new DateTime(2026, 6, 30) });

        Assert.Equal(9m, originalPeriod.OperatingNetMovement);
        Assert.Equal(-9m, reversalPeriod.OperatingNetMovement);
        Assert.Equal(50, Assert.Single(reversalPeriod.Sections.Single(x => x.ActivityType == "Operating").Rows).ReversalOfJournalEntryId);
    }

    [Fact]
    public async Task ComparativeCashFlow_UsesSameClassificationLogicForBothPeriods()
    {
        await using var db = CreateDb();
        await SeedCoreAsync(db);
        db.JournalEntries.AddRange(
            Journal(60, new DateTime(2026, 7, 1), "SalePosting", "Sale", 1, 100, 0),
            Journal(61, new DateTime(2026, 6, 1), "SalePosting", "Sale", 1, 40, 0),
            Journal(62, new DateTime(2026, 6, 2), "ManualJournal", "ManualJournal", 1, 2, 0));
        await db.SaveChangesAsync();

        var report = await GetComparativeCashFlowAsync(db, new CashFlowReportFilterDto
        {
            DateFrom = new DateTime(2026, 7, 1),
            DateTo = new DateTime(2026, 7, 31),
            ComparisonDateFrom = new DateTime(2026, 6, 1),
            ComparisonDateTo = new DateTime(2026, 6, 30)
        });

        Assert.Equal(100m, report.Current.OperatingNetMovement);
        Assert.Equal(40m, report.Comparison.OperatingNetMovement);
        Assert.Equal(2m, report.Comparison.UnclassifiedNetMovement);
    }

    [Fact]
    public async Task CashFlow_IsTenantScoped()
    {
        await using var db = CreateDb();
        await SeedCoreAsync(db);
        db.JournalEntries.AddRange(
            Journal(70, new DateTime(2026, 8, 1), "SalePosting", "Sale", 1, 12, 0),
            Journal(71, new DateTime(2026, 8, 1), "SalePosting", "Sale", 1, 999, 0, businessId: 2));
        await db.SaveChangesAsync();

        var report = await GetCashFlowAsync(db, new CashFlowReportFilterDto { DateFrom = new DateTime(2026, 8, 1), DateTo = new DateTime(2026, 8, 31) });

        Assert.Equal(12m, report.OperatingNetMovement);
        Assert.DoesNotContain(report.Sections.SelectMany(x => x.Rows), row => row.JournalEntryId == 71);
    }

    private static async Task<CashFlowReportDto> GetCashFlowAsync(
        RetailPOSDbContext db,
        CashFlowReportFilterDto filter)
    {
        var result = await SendQueryAsync(
            db,
            new GetCashFlowQuery(ToFilter(filter)));
        return RetailPOS.API.Services.AccountingReportReadModelMapper.ToApi(result);
    }

    private static async Task<ComparativeCashFlowReportDto> GetComparativeCashFlowAsync(
        RetailPOSDbContext db,
        CashFlowReportFilterDto filter)
    {
        var result = await SendQueryAsync(
            db,
            new GetComparativeCashFlowQuery(ToFilter(filter)));
        return RetailPOS.API.Services.AccountingReportReadModelMapper.ToApi(result);
    }

    private static CashFlowFilter ToFilter(CashFlowReportFilterDto filter) =>
        new(
            filter.DateFrom,
            filter.DateTo,
            filter.LocationId,
            filter.LocationType,
            filter.JournalType,
            filter.ReferenceType,
            filter.Reference,
            filter.ComparisonDateFrom,
            filter.ComparisonDateTo);

    private static async Task<TResponse> SendQueryAsync<TResponse>(
        RetailPOSDbContext db,
        IRequest<TResponse> query)
    {
        var tenantScope = new Mock<IAccountingReportTenantScope>();
        tenantScope.Setup(scope => scope.GetBusinessIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1L);
        var resolver = new Mock<ICashBookPostingAccountResolver>();
        resolver.Setup(accountResolver => accountResolver.ResolveAsync(
                It.IsAny<string>(),
                It.IsAny<long?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string slot, long? _, CancellationToken _) =>
                new CashBookAccountResolution(slot, slot switch
                {
                    PostingAccountSlots.Cash => 1,
                    PostingAccountSlots.PettyCash => 2,
                    PostingAccountSlots.Bank => 3,
                    _ => throw new InvalidOperationException($"Unexpected Cash Flow account slot '{slot}'.")
                }));
        var ownerAccounts = new Mock<ICashFlowOwnerAccountsProvider>();
        ownerAccounts.Setup(provider => provider.GetAsync(
                It.IsAny<long>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CashFlowOwnerAccounts(6, null));

        var services = new ServiceCollection();
        services.AddApplication();
        services.AddSingleton<ICashBookReadStore>(new CashBookReadStore(db));
        services.AddSingleton(tenantScope.Object);
        services.AddSingleton(resolver.Object);
        services.AddSingleton(ownerAccounts.Object);
        services.AddSingleton(TestCompanySettings.Provider());
        using var serviceProvider = services.BuildServiceProvider();
        return await serviceProvider.GetRequiredService<ISender>().Send(query);
    }

    private static async Task SeedCoreAsync(RetailPOSDbContext db)
    {
        db.Businesses.AddRange(new Business { Id = 1, Name = "Cash Flow" }, new Business { Id = 2, Name = "Other" });
        db.Outlets.Add(new Outlet { Id = 11, BusinessId = 1, Name = "Outlet", Address = "A" });
        db.ChartAccounts.AddRange(
            Account(1, "1000", "Cash", ChartAccountTypes.Asset, NormalBalanceTypes.Debit),
            Account(2, "1010", "Petty Cash", ChartAccountTypes.Asset, NormalBalanceTypes.Debit),
            Account(3, "1020", "Bank", ChartAccountTypes.Asset, NormalBalanceTypes.Debit),
            Account(4, "2000", "Payable", ChartAccountTypes.Liability, NormalBalanceTypes.Credit),
            Account(5, "4000", "Revenue", ChartAccountTypes.Income, NormalBalanceTypes.Credit),
            Account(6, "3000", "Owner Capital", ChartAccountTypes.Equity, NormalBalanceTypes.Credit),
            Account(7, "1030", "Card Clearing", ChartAccountTypes.Asset, NormalBalanceTypes.Debit),
            Account(8, "1500", "Fixed Asset", ChartAccountTypes.Asset, NormalBalanceTypes.Debit));
        await db.SaveChangesAsync();
    }

    private static ChartAccount Account(long id, string code, string name, string type, string normalBalance) => new()
    {
        Id = id,
        BusinessId = 1,
        AccountCode = code,
        AccountName = name,
        AccountType = type,
        NormalBalanceType = normalBalance,
        IsActive = true,
        AllowManualPosting = true
    };

    private static JournalEntry Journal(long id, DateTime date, string journalType, string referenceType, long liquidityAccountId, decimal debit, decimal credit, string status = JournalEntryStatuses.Posted, long? reversedBy = null, long? reversalOf = null, long counterpartAccountId = 4, long? secondLiquidityAccountId = null, decimal secondLiquidityDebit = 0, decimal secondLiquidityCredit = 0, string? description = null, long businessId = 1) => new()
    {
        Id = id,
        BusinessId = businessId,
        JournalNumber = $"JR-{id:D3}",
        JournalType = journalType,
        ReferenceType = referenceType,
        ReferenceId = id.ToString(),
        PostingDate = date,
        BusinessDate = date,
        Status = status,
        Description = description ?? journalType,
        OutletId = 11,
        ReversedByJournalEntryId = reversedBy,
        ReversalOfJournalEntryId = reversalOf,
        Lines = secondLiquidityAccountId.HasValue
            ?
            [
                new JournalEntryLine { AccountId = liquidityAccountId, DebitAmount = debit, CreditAmount = credit, OutletId = 11, Description = journalType },
                new JournalEntryLine { AccountId = secondLiquidityAccountId.Value, DebitAmount = secondLiquidityDebit, CreditAmount = secondLiquidityCredit, OutletId = 11, Description = journalType }
            ]
            :
            [
                new JournalEntryLine { AccountId = liquidityAccountId, DebitAmount = debit, CreditAmount = credit, OutletId = 11, Description = journalType },
                new JournalEntryLine { AccountId = counterpartAccountId, DebitAmount = credit, CreditAmount = debit, OutletId = 11, Description = "Counterpart" }
            ]
    };

    private static RetailPOSDbContext CreateDb() => new(new DbContextOptionsBuilder<RetailPOSDbContext>().UseInMemoryDatabase($"P308_{Guid.NewGuid()}").Options);
}
