using Microsoft.EntityFrameworkCore;
using Moq;
using RetailPOS.API.DTOs.Reports;
using RetailPOS.API.Services;
using RetailPOS.Application.AccountingReports.Abstractions;
using RetailPOS.Application.AccountingReports.Queries;
using RetailPOS.Application.AccountingReports.ReadModels;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.AccountingReports;
using RetailPOS.Infrastructure.Data;

using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Accounting;

public class P206ValuationReportServiceTests
{
    [Fact]
    public async Task Cogs_UsesOnlyPostedConfiguredAccountLines_AndReversalsReduceTotal()
    {
        await using var db = CreateDb();
        await SeedAsync(db);
        db.JournalEntries.AddRange(
            Journal(100, 20, 0, "Sale"),
            Journal(101, 0, 8, "SalesReturn"),
            Journal(102, 0, 3, "SaleExchange"),
            new JournalEntry { Id = 103, BusinessId = 1, Status = JournalEntryStatuses.Draft, PostingDate = new DateTime(2026, 1, 3), Lines = new List<JournalEntryLine> { new() { AccountId = 10, DebitAmount = 99 } } });
        await db.SaveChangesAsync();

        var result = await GetCogsAsync(db, new ValuationReportFilterDto { DateFrom = new DateTime(2026, 1, 1), DateTo = new DateTime(2026, 1, 31) });

        Assert.Equal(9m, result.TotalCogs);
        Assert.Equal(9m, result.GeneralLedgerBalance);
        Assert.Equal(0m, result.ReconciliationDifference);
        Assert.Equal(3, result.Rows.Count);
        Assert.Contains(result.Rows, row => row.ReferenceType == "SalesReturn" && row.NetCogs == -8m);
        Assert.Contains(result.Rows, row => row.ReferenceType == "SaleExchange" && row.NetCogs == -3m);
    }

    [Fact]
    public async Task InventoryValuation_ReplaysWeightedAverage_AsOfBusinessDate_AndExcludesUnvalued()
    {
        await using var db = CreateDb();
        await SeedAsync(db);
        db.StockLedgers.AddRange(
            Ledger(1, 10, 0, 10m, new DateTime(2026, 1, 1), 1),
            Ledger(2, 10, 0, 20m, new DateTime(2026, 1, 2), 2),
            Ledger(3, 0, 5, 0m, new DateTime(2026, 1, 3), 3),
            Ledger(4, 1, 0, 0m, new DateTime(2026, 1, 4), 4, reliable: false));
        db.JournalEntries.AddRange(
            new JournalEntry
            {
                Id = 199, BusinessId = 1, Status = JournalEntryStatuses.Posted, PostingDate = new DateTime(2026, 1, 3),
                Lines = new List<JournalEntryLine> { new() { AccountId = 20, DebitAmount = 225m } }
            },
            new JournalEntry
        {
            Id = 200, BusinessId = 1, Status = JournalEntryStatuses.Posted, PostingDate = new DateTime(2026, 1, 4),
            Lines = new List<JournalEntryLine> { new() { AccountId = 20, DebitAmount = 15m } }
        });
        await db.SaveChangesAsync();

        var asOfReceipt = await GetInventoryValuationAsync(db, new ValuationReportFilterDto { DateTo = new DateTime(2026, 1, 2) });
        var fullyValued = await GetInventoryValuationAsync(db, new ValuationReportFilterDto { DateTo = new DateTime(2026, 1, 3) });
        var legacyIncluded = await GetInventoryValuationAsync(db, new ValuationReportFilterDto { DateTo = new DateTime(2026, 1, 4) });

        Assert.Equal(20, asOfReceipt.Rows.Single().Quantity);
        Assert.Equal(300m, asOfReceipt.TotalInventoryValue);
        Assert.Equal(225m, fullyValued.TotalInventoryValue);
        Assert.Equal(225m, fullyValued.GeneralLedgerBalance);
        Assert.Equal(0m, fullyValued.ReconciliationDifference);
        Assert.Equal(15, legacyIncluded.Rows.Single().Quantity);
        Assert.Equal(225m, legacyIncluded.TotalInventoryValue);
        Assert.Equal(240m, legacyIncluded.GeneralLedgerBalance);
        Assert.Equal(-15m, legacyIncluded.ReconciliationDifference);
        Assert.Single(legacyIncluded.UnvaluedMovements);
    }

    [Fact]
    public async Task InventoryValuation_TransferPreservesBusinessValue_AndLocationFilterIsolated()
    {
        await using var db = CreateDb();
        await SeedAsync(db);
        db.StockLedgers.AddRange(
            Ledger(1, 10, 0, 10m, new DateTime(2026, 1, 1), 1, locationId: 11),
            Ledger(2, 0, 4, 10m, new DateTime(2026, 1, 2), 2, locationId: 11),
            Ledger(3, 4, 0, 10m, new DateTime(2026, 1, 2), 1, locationId: 21, locationType: "warehouse"));
        await db.SaveChangesAsync();

        var allLocations = await GetInventoryValuationAsync(db, new ValuationReportFilterDto { DateTo = new DateTime(2026, 1, 2) });
        var outletOnly = await GetInventoryValuationAsync(db, new ValuationReportFilterDto { DateTo = new DateTime(2026, 1, 2), OutletId = 11 });

        Assert.Equal(100m, allLocations.TotalInventoryValue);
        Assert.Equal(60m, outletOnly.TotalInventoryValue);
        Assert.Single(outletOnly.Rows);
    }

    [Fact]
    public async Task InventoryValuation_UsesStockGainAndShortageMovementsWithoutCurrentCost()
    {
        await using var db = CreateDb();
        await SeedAsync(db);
        db.StockLedgers.AddRange(
            Ledger(1, 10, 0, 10m, new DateTime(2026, 2, 1), 1),
            Ledger(2, 2, 0, 10m, new DateTime(2026, 2, 2), 2),
            Ledger(3, 0, 3, 0m, new DateTime(2026, 2, 3), 3));
        db.JournalEntries.Add(new JournalEntry
        {
            Id = 300, BusinessId = 1, Status = JournalEntryStatuses.Posted, PostingDate = new DateTime(2026, 2, 3),
            Lines = new List<JournalEntryLine> { new() { AccountId = 20, DebitAmount = 90m } }
        });
        await db.SaveChangesAsync();

        var result = await GetInventoryValuationAsync(db, new ValuationReportFilterDto { DateTo = new DateTime(2026, 2, 3) });

        Assert.Equal(9, result.Rows.Single().Quantity);
        Assert.Equal(90m, result.TotalInventoryValue);
        Assert.Equal(0m, result.ReconciliationDifference);
    }

    [Fact]
    public async Task InventoryValuation_ReplaysSameDayMovementsBySequenceDeterministically()
    {
        await using var db = CreateDb();
        await SeedAsync(db);
        var day = new DateTime(2026, 3, 1);
        db.StockLedgers.AddRange(
            Ledger(30, 10, 0, 10m, day, 1),
            Ledger(10, 0, 5, 0m, day, 2),
            Ledger(20, 10, 0, 20m, day, 3));
        await db.SaveChangesAsync();

        var result = await GetInventoryValuationAsync(db, new ValuationReportFilterDto { DateTo = day });

        Assert.Equal(15, result.Rows.Single().Quantity);
        Assert.Equal(250m, result.TotalInventoryValue);
    }

    [Fact]
    public async Task CogsAndValuation_ApplyProductVariantAndLocationFilters_AndExcludeRoundingAccount()
    {
        await using var db = CreateDb();
        await SeedAsync(db);
        db.Businesses.Add(new Business { Id = 2, Name = "Other Business" });
        db.Warehouses.Add(new Warehouse { Id = 22, BusinessId = 2, Name = "Other Warehouse", Address = "B" });
        db.Products.Add(new Product { Id = 2, BusinessId = 1, Name = "Other", CategoryId = 1 });
        db.ProductVariants.Add(new ProductVariant { Id = 2, BusinessId = 1, ProductId = 2, Name = "Other Variant", Sku = "P2-06-2" });
        db.Sales.Add(new Sale { Id = 400, OutletId = 11, Items = new List<SaleItem> { new() { VariantId = 1, Quantity = 1 } } });
        db.JournalEntries.Add(new JournalEntry
        {
            Id = 401, BusinessId = 1, Status = JournalEntryStatuses.Posted, PostingDate = new DateTime(2026, 4, 1), ReferenceType = "Sale", ReferenceId = "400",
            Lines = new List<JournalEntryLine> { new() { AccountId = 10, DebitAmount = 10m }, new() { AccountId = 30, DebitAmount = 1m } }
        });
        db.StockLedgers.AddRange(
            Ledger(1, 2, 0, 10m, new DateTime(2026, 4, 1), 1),
            new StockLedger { Id = 2, VariantId = 2, LocationId = 21, LocationType = "warehouse", QtyIn = 3, UnitCost = 20m, MovementValue = 60m, IsCostValuationReliable = true, BusinessDate = new DateTime(2026, 4, 1), MovementSequence = 1, TransactionType = "test", ReferenceType = "test" },
            new StockLedger { Id = 3, VariantId = 2, LocationId = 22, LocationType = "warehouse", QtyIn = 9, UnitCost = 111m, MovementValue = 999m, IsCostValuationReliable = true, BusinessDate = new DateTime(2026, 4, 1), MovementSequence = 1, TransactionType = "test", ReferenceType = "test" });
        await db.SaveChangesAsync();

        var cogs = await GetCogsAsync(db, new ValuationReportFilterDto { VariantId = 1, DateTo = new DateTime(2026, 4, 1) });
        var valuation = await GetInventoryValuationAsync(db, new ValuationReportFilterDto { VariantId = 1, LocationId = 11, LocationType = "outlet", DateTo = new DateTime(2026, 4, 1) });
        var productValuation = await GetInventoryValuationAsync(db, new ValuationReportFilterDto { ProductId = 1, OutletId = 11, DateTo = new DateTime(2026, 4, 1) });
        var warehouseValuation = await GetInventoryValuationAsync(db, new ValuationReportFilterDto { WarehouseId = 21, DateTo = new DateTime(2026, 4, 1) });
        var tenantValuation = await GetInventoryValuationAsync(db, new ValuationReportFilterDto { DateTo = new DateTime(2026, 4, 1) });

        Assert.Equal(10m, cogs.TotalCogs);
        Assert.Equal(10m, cogs.GeneralLedgerBalance);
        Assert.Single(valuation.Rows);
        Assert.Equal(20m, valuation.TotalInventoryValue);
        Assert.Equal(20m, productValuation.TotalInventoryValue);
        Assert.Equal(60m, warehouseValuation.TotalInventoryValue);
        Assert.Equal(80m, tenantValuation.TotalInventoryValue);
    }

    private static async Task<CogsReportReadModel> GetCogsAsync(
        RetailPOSDbContext db,
        ValuationReportFilterDto filter)
    {
        var tenantScope = new Mock<IAccountingReportTenantScope>();
        tenantScope.Setup(scope => scope.GetBusinessIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1L);
        var handler = new GetCogsReportQueryHandler(
            new CogsReportReadStore(db, TestCompanySettings.Provider()),
            tenantScope.Object);
        return await handler.Handle(
            new GetCogsReportQuery(new CogsReportFilter(
                filter.DateFrom,
                filter.DateTo,
                filter.OutletId,
                filter.WarehouseId,
                filter.ProductId,
                filter.VariantId)),
            CancellationToken.None);
    }

    private static async Task<InventoryValuationReportDto> GetInventoryValuationAsync(
        RetailPOSDbContext db,
        ValuationReportFilterDto filter)
    {
        var tenantScope = new Mock<IAccountingReportTenantScope>();
        tenantScope.Setup(scope => scope.GetBusinessIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1L);
        var handler = new GetInventoryValuationQueryHandler(
            new InventoryValuationReadStore(db, TestCompanySettings.Provider()),
            tenantScope.Object);
        var result = await handler.Handle(
            new GetInventoryValuationQuery(new InventoryValuationFilter(
                filter.DateTo,
                filter.OutletId,
                filter.WarehouseId,
                filter.LocationId,
                filter.LocationType,
                filter.ProductId,
                filter.VariantId)),
            CancellationToken.None);
        return RetailPOS.API.Services.AccountingReportReadModelMapper.ToApi(result);
    }

    private static async Task SeedAsync(RetailPOSDbContext db)
    {
        db.Businesses.Add(new Business { Id = 1, Name = "P2-06" });
        db.Outlets.Add(new Outlet { Id = 11, BusinessId = 1, Name = "Outlet", Address = "A" });
        db.Warehouses.Add(new Warehouse { Id = 21, BusinessId = 1, Name = "Warehouse", Address = "A" });
        db.Categories.Add(new Category { Id = 1, BusinessId = 1, Name = "Category" });
        db.Products.Add(new Product { Id = 1, BusinessId = 1, Name = "Product", CategoryId = 1 });
        db.ProductVariants.Add(new ProductVariant { Id = 1, BusinessId = 1, ProductId = 1, Name = "Variant", Sku = "P2-06-1" });
        db.AccountingConfigurations.Add(new AccountingConfiguration { Id = 1, BusinessId = 1, CogsAccountId = 10, InventoryAccountId = 20, RoundingDifferenceAccountId = 30 });
        await db.SaveChangesAsync();
    }

    private static JournalEntry Journal(long id, decimal debit, decimal credit, string referenceType) => new()
    {
        Id = id, BusinessId = 1, Status = JournalEntryStatuses.Posted, JournalNumber = $"JR-{id}", JournalType = "Sale",
        PostingDate = new DateTime(2026, 1, 2), ReferenceType = referenceType, ReferenceId = id.ToString(),
        Lines = new List<JournalEntryLine> { new() { AccountId = 10, DebitAmount = debit, CreditAmount = credit } }
    };

    private static StockLedger Ledger(long id, int quantityIn, int quantityOut, decimal unitCost, DateTime date, long sequence, long locationId = 11, string locationType = "outlet", bool reliable = true) => new()
    {
        Id = id, VariantId = 1, LocationId = locationId, LocationType = locationType, QtyIn = quantityIn, QtyOut = quantityOut,
        BalanceAfter = quantityIn - quantityOut, UnitCost = unitCost, MovementValue = (quantityIn + quantityOut) * unitCost,
        IsCostValuationReliable = reliable, BusinessDate = date, MovementSequence = sequence, TransactionType = "test", ReferenceType = "test", ReferenceId = id, CreatedAt = date
    };

    private static RetailPOSDbContext CreateDb() => new(new DbContextOptionsBuilder<RetailPOSDbContext>()
        .UseInMemoryDatabase($"P206_{Guid.NewGuid()}").Options);
}