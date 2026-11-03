using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MediatR;
using Moq;
using RetailPOS.Application;
using RetailPOS.Application.AccountingReports.Abstractions;
using RetailPOS.Application.AccountingReports.Queries;
using RetailPOS.Application.AccountingReports.ReadModels;
using RetailPOS.API.DTOs.Reports;
using RetailPOS.API.Services;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.AccountingReports;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Tests.Accounting;

public class P308ProfitabilityReportServiceTests
{
    [Fact]
    public async Task BusinessProfitability_ReconcilesToAuthoritativeProfitLoss()
    {
        await using var db = CreateDb();
        await SeedProfitabilityAsync(db);

        var report = await GetBusinessProfitabilityAsync(db, Filter());

        Assert.Equal(100m, report.Revenue);
        Assert.Equal(60m, report.CostOfGoodsSold);
        Assert.Equal(25m, report.Expenses);
        Assert.Equal(15m, report.NetProfit);
        Assert.Equal(report.AuthoritativePnlNetProfit, report.NetProfit);
        Assert.Equal(0m, report.ReconciliationDifference);
    }

    [Fact]
    public async Task OutletProfitability_KeepsDimensionlessActivityUnallocated_AndReconcilesToBusinessTotal()
    {
        await using var db = CreateDb();
        await SeedProfitabilityAsync(db);

        var report = await GetOutletProfitabilityAsync(db, Filter());
        var outlet = Assert.Single(report.Outlets);

        Assert.Equal(11, outlet.OutletId);
        Assert.Equal(100m, outlet.Revenue);
        Assert.Equal(60m, outlet.CostOfGoodsSold);
        Assert.Equal(0m, outlet.Expenses);
        Assert.Equal(40m, outlet.NetProfit);
        Assert.Equal(25m, report.Unallocated.Expenses);
        Assert.Equal(-25m, report.Unallocated.NetProfit);
        Assert.Equal(15m, report.AttributedNetProfit);
        Assert.Equal(report.BusinessNetProfit, report.AttributedNetProfit);
        Assert.Equal(0m, report.ReconciliationDifference);
    }

    [Fact]
    public async Task ProductContribution_UsesSaleItemEvidence_AndDoesNotAllocateSharedExpenses()
    {
        await using var db = CreateDb();
        await SeedProfitabilityAsync(db);

        var report = await GetProductContributionAsync(db, Filter());
        var product = Assert.Single(report.Products);
        var category = Assert.Single(report.Categories);

        Assert.Equal(501, product.ProductId);
        Assert.Equal(100m, product.Revenue);
        Assert.Equal(60m, product.CostOfGoodsSold);
        Assert.Equal(40m, product.Contribution);
        Assert.Equal(40m, product.MarginPercent);
        Assert.Equal(product.Contribution, category.Contribution);
        Assert.Contains("shared expenses", report.ContributionBasis, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(100m, report.AuthoritativeRevenue);
        Assert.Equal(60m, report.AuthoritativeCostOfGoodsSold);
        Assert.Equal(0m, report.UnallocatedRevenue);
        Assert.Equal(0m, report.UnallocatedCostOfGoodsSold);
        Assert.Equal(0m, report.RevenueReconciliationDifference);
        Assert.Equal(0m, report.CogsReconciliationDifference);
        Assert.Contains("shared expenses", report.HistoricalLimitation!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProductContribution_SurfacesUnallocatedRevenueAndCogs_WhenJournalEvidenceHasNoProductSource()
    {
        await using var db = CreateDb();
        await SeedProfitabilityAsync(db);
        db.JournalEntries.Add(Journal(90, new DateTime(2026, 7, 12), "SalePosting", "Sale", "999", 30m, 10m, 0m, 11));
        await db.SaveChangesAsync();

        var report = await GetProductContributionAsync(db, Filter());

        Assert.Equal(100m, report.AttributedRevenue);
        Assert.Equal(60m, report.AttributedCostOfGoodsSold);
        Assert.Equal(130m, report.AuthoritativeRevenue);
        Assert.Equal(70m, report.AuthoritativeCostOfGoodsSold);
        Assert.Equal(30m, report.UnallocatedRevenue);
        Assert.Equal(10m, report.UnallocatedCostOfGoodsSold);
    }

    [Fact]
    public async Task ComparativeProfitability_UsesSameLogicForCurrentAndComparisonPeriods()
    {
        await using var db = CreateDb();
        await SeedProfitabilityAsync(db);
        db.JournalEntries.Add(Journal(80, new DateTime(2026, 6, 10), "SalePosting", "Sale", "80", 50m, 20m, 5m, 11));
        await db.SaveChangesAsync();

        var report = await GetComparativeBusinessProfitabilityAsync(db, new ProfitabilityReportFilterDto
        {
            DateFrom = new DateTime(2026, 7, 1),
            DateTo = new DateTime(2026, 7, 31),
            ComparisonDateFrom = new DateTime(2026, 6, 1),
            ComparisonDateTo = new DateTime(2026, 6, 30)
        });

        Assert.Equal(15m, report.Current.NetProfit);
        Assert.Equal(25m, report.Comparison.NetProfit);
    }

    [Fact]
    public async Task ComparativeOutletProfitability_ReconcilesBothPeriodsIndependently()
    {
        await using var db = CreateDb();
        await SeedProfitabilityAsync(db);
        db.JournalEntries.Add(
            Journal(80, new DateTime(2026, 6, 10), "SalePosting", "Sale", "80", 50m, 20m, 5m, 11));
        await db.SaveChangesAsync();

        var report = await GetComparativeOutletProfitabilityAsync(
            db,
            new ProfitabilityReportFilterDto
            {
                DateFrom = new DateTime(2026, 7, 1),
                DateTo = new DateTime(2026, 7, 31),
                ComparisonDateFrom = new DateTime(2026, 6, 1),
                ComparisonDateTo = new DateTime(2026, 6, 30)
            });

        Assert.Equal(0m, report.Current.ReconciliationDifference);
        Assert.Equal(0m, report.Comparison.ReconciliationDifference);
        Assert.Equal(15m, report.Current.BusinessNetProfit);
        Assert.Equal(25m, report.Comparison.BusinessNetProfit);
    }

    [Fact]
    public async Task ProductContribution_FiltersSaleItemAttribution_AndKeepsJournalTotalsAuthoritative()
    {
        await using var db = CreateDb();
        await SeedProfitabilityAsync(db);

        var report = await GetProductContributionAsync(
            db,
            new ProfitabilityReportFilterDto
            {
                DateFrom = new DateTime(2026, 7, 1),
                DateTo = new DateTime(2026, 7, 31),
                ProductId = 999
            });

        Assert.Empty(report.Products);
        Assert.Empty(report.Categories);
        Assert.Equal(0m, report.AttributedRevenue);
        Assert.Equal(100m, report.AuthoritativeRevenue);
        Assert.Equal(100m, report.UnallocatedRevenue);
        Assert.Equal(60m, report.UnallocatedCostOfGoodsSold);
        Assert.Equal(0m, report.RevenueReconciliationDifference);
        Assert.Equal(0m, report.CogsReconciliationDifference);
    }

    [Fact]
    public async Task ProductContribution_UsesZeroForMissingHistoricalCost()
    {
        await using var db = CreateDb();
        await SeedProfitabilityAsync(db);
        db.JournalEntries.Add(
            Journal(91, new DateTime(2026, 7, 12), "SalePosting", "Sale", "91", 0m, 0m, 0m, 11));
        db.Sales.Add(new Sale
        {
            Id = 91,
            SaleNumber = "S-091",
            OutletId = 11,
            SaleDate = new DateTime(2026, 7, 12),
            Status = "completed",
            CashierId = 1,
            Items =
            [
                new SaleItem
                {
                    Id = 791,
                    VariantId = 601,
                    Quantity = 1,
                    UnitPrice = 10m,
                    Subtotal = 10m
                }
            ]
        });
        await db.SaveChangesAsync();

        var report = await GetProductContributionAsync(db, Filter());
        var product = Assert.Single(report.Products);

        Assert.Equal(110m, product.Revenue);
        Assert.Equal(60m, product.CostOfGoodsSold);
        Assert.Equal(50m, product.Contribution);
        Assert.Equal(-10m, report.UnallocatedRevenue);
    }

    [Fact]
    public async Task Profitability_IsTenantScoped()
    {
        await using var db = CreateDb();
        await SeedProfitabilityAsync(db);
        db.JournalEntries.Add(Journal(70, new DateTime(2026, 7, 10), "SalePosting", "Sale", "70", 999m, 0m, 0m, 11, businessId: 2));
        await db.SaveChangesAsync();

        var report = await GetBusinessProfitabilityAsync(db, Filter());

        Assert.Equal(100m, report.Revenue);
    }

    private static ProfitabilityReportFilterDto Filter() => new() { DateFrom = new DateTime(2026, 7, 1), DateTo = new DateTime(2026, 7, 31) };

    private static async Task<BusinessProfitabilityReportDto> GetBusinessProfitabilityAsync(
        RetailPOSDbContext db,
        ProfitabilityReportFilterDto filter)
    {
        var result = await SendQueryAsync(
            db,
            new GetBusinessProfitabilityQuery(ToFilter(filter)));
        return AccountingReportReadModelMapper.ToApi(result);
    }

    private static async Task<OutletProfitabilityReportDto> GetOutletProfitabilityAsync(
        RetailPOSDbContext db,
        ProfitabilityReportFilterDto filter)
    {
        var result = await SendQueryAsync(
            db,
            new GetOutletProfitabilityQuery(ToFilter(filter)));
        return AccountingReportReadModelMapper.ToApi(result);
    }

    private static async Task<ComparativeBusinessProfitabilityReportDto>
        GetComparativeBusinessProfitabilityAsync(
            RetailPOSDbContext db,
            ProfitabilityReportFilterDto filter)
    {
        var result = await SendQueryAsync(
            db,
            new GetComparativeBusinessProfitabilityQuery(ToFilter(filter)));
        return AccountingReportReadModelMapper.ToApi(result);
    }

    private static async Task<ComparativeOutletProfitabilityReportDto>
        GetComparativeOutletProfitabilityAsync(
            RetailPOSDbContext db,
            ProfitabilityReportFilterDto filter)
    {
        var result = await SendQueryAsync(
            db,
            new GetComparativeOutletProfitabilityQuery(ToFilter(filter)));
        return AccountingReportReadModelMapper.ToApi(result);
    }

    private static async Task<ProductContributionReportDto> GetProductContributionAsync(
        RetailPOSDbContext db,
        ProfitabilityReportFilterDto filter)
    {
        var result = await SendQueryAsync(
            db,
            new GetProductContributionQuery(ToFilter(filter)));
        return AccountingReportReadModelMapper.ToApi(result);
    }

    private static ProfitabilityFilter ToFilter(ProfitabilityReportFilterDto filter) =>
        new(
            filter.DateFrom,
            filter.DateTo,
            filter.ComparisonDateFrom,
            filter.ComparisonDateTo,
            filter.LocationId,
            filter.LocationType,
            filter.ProductId,
            filter.CategoryId);

    private static async Task<TResponse> SendQueryAsync<TResponse>(
        RetailPOSDbContext db,
        IRequest<TResponse> query)
    {
        var tenantScope = new Mock<IAccountingReportTenantScope>();
        tenantScope.Setup(scope => scope.GetBusinessIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1L);
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddSingleton<IProfitabilityReportReadStore>(
            new ProfitabilityReportReadStore(db));
        services.AddSingleton<IProductContributionReadStore>(
            new ProductContributionReadStore(db));
        services.AddSingleton(tenantScope.Object);
        using var serviceProvider = services.BuildServiceProvider();
        return await serviceProvider.GetRequiredService<ISender>().Send(query);
    }

    private static async Task SeedProfitabilityAsync(RetailPOSDbContext db)
    {
        db.Businesses.AddRange(new Business { Id = 1, Name = "Profitability" }, new Business { Id = 2, Name = "Other" });
        db.Outlets.Add(new Outlet { Id = 11, BusinessId = 1, Name = "Outlet", Address = "A" });
        db.Categories.Add(new Category { Id = 401, BusinessId = 1, Name = "Category" });
        db.Products.Add(new Product { Id = 501, BusinessId = 1, Name = "Product", ProductCode = "P-1", CategoryId = 401 });
        db.ProductVariants.Add(new ProductVariant { Id = 601, BusinessId = 1, ProductId = 501, Name = "Default", Sku = "P-1-DEFAULT" });
        var saleItem = new SaleItem { Id = 701, VariantId = 601, Quantity = 2, UnitPrice = 50m, Subtotal = 100m };
        saleItem.CaptureHistoricalUnitCost(30m);
        db.Sales.Add(new Sale { Id = 10, SaleNumber = "S-001", OutletId = 11, SaleDate = new DateTime(2026, 7, 10), Status = "completed", CashierId = 1, Items = [saleItem] });
        db.ChartAccounts.AddRange(
            Account(1000, "Cash", ChartAccountTypes.Asset, NormalBalanceTypes.Debit),
            Account(2000, "Revenue", ChartAccountTypes.Income, NormalBalanceTypes.Credit),
            Account(3000, "Expense", ChartAccountTypes.Expense, NormalBalanceTypes.Debit),
            Account(4000, "COGS", ChartAccountTypes.CostOfGoodsSold, NormalBalanceTypes.Debit),
            Account(5000, "Inventory", ChartAccountTypes.Asset, NormalBalanceTypes.Debit));
        db.JournalEntries.AddRange(
            Journal(10, new DateTime(2026, 7, 10), "SalePosting", "Sale", "10", 100m, 60m, 0m, 11),
            Journal(20, new DateTime(2026, 7, 11), "ManualJournal", "ManualJournal", "20", 0m, 0m, 25m, null));
        await db.SaveChangesAsync();
    }

    private static ChartAccount Account(long id, string name, string type, string normalBalance) => new()
    {
        Id = id,
        BusinessId = 1,
        AccountCode = id.ToString(),
        AccountName = name,
        AccountType = type,
        NormalBalanceType = normalBalance,
        IsActive = true,
        AllowManualPosting = true
    };

    private static JournalEntry Journal(long id, DateTime date, string journalType, string referenceType, string referenceId, decimal revenue, decimal cogs, decimal expense, long? outletId, long businessId = 1) => new()
    {
        Id = id,
        BusinessId = businessId,
        JournalNumber = $"JR-{id:D3}",
        JournalType = journalType,
        ReferenceType = referenceType,
        ReferenceId = referenceId,
        PostingDate = date,
        BusinessDate = date,
        Status = JournalEntryStatuses.Posted,
        OutletId = outletId,
        Lines = Lines(revenue, cogs, expense, outletId)
    };

    private static List<JournalEntryLine> Lines(decimal revenue, decimal cogs, decimal expense, long? outletId)
    {
        var lines = new List<JournalEntryLine>();
        if (revenue > 0)
        {
            lines.Add(new JournalEntryLine { AccountId = 1000, DebitAmount = revenue, OutletId = outletId });
            lines.Add(new JournalEntryLine { AccountId = 2000, CreditAmount = revenue, OutletId = outletId });
        }
        if (cogs > 0)
        {
            lines.Add(new JournalEntryLine { AccountId = 4000, DebitAmount = cogs, OutletId = outletId });
            lines.Add(new JournalEntryLine { AccountId = 5000, CreditAmount = cogs, OutletId = outletId });
        }
        if (expense > 0)
        {
            lines.Add(new JournalEntryLine { AccountId = 3000, DebitAmount = expense, OutletId = outletId });
            lines.Add(new JournalEntryLine { AccountId = 1000, CreditAmount = expense, OutletId = outletId });
        }
        return lines;
    }

    private static RetailPOSDbContext CreateDb() => new(new DbContextOptionsBuilder<RetailPOSDbContext>().UseInMemoryDatabase($"P308Profitability_{Guid.NewGuid()}").Options);
}
