using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using RetailPOS.API.DTOs.Sale;
using RetailPOS.API.Services;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.Data;
using InventoryEntity = RetailPOS.Core.Entities.Inventory;

namespace RetailPOS.Tests.Sales;

public class SaleServiceHistoricalCostSnapshotTests
{
    [Fact]
    public async Task CreateAsync_PersistsHistoricalUnitCostSnapshotAndKeepsItStable()
    {
        var db = CreateDbContext();
        var product = new Product
        {
            BusinessId = 1,
            Name = "Widget",
            Sku = "W-001",
            Barcode = "W001",
            BasePrice = 25m,
            CostPrice = 10m,
            Status = ProductStatus.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        var variant = new ProductVariant
        {
            BusinessId = 1,
            Product = product,
            Name = "Default",
            Sku = "W-001-DEFAULT",
            CostAdjustment = 1.5m,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var inventory = new InventoryEntity
        {
            Variant = variant,
            LocationId = 100,
            LocationType = "outlet",
            Quantity = 5,
            LowStockThreshold = 1
        };

        var outlet = new Outlet
        {
            Id = 100,
            BusinessId = 1,
            Name = "Outlet A",
            Address = "Address",
            ContactNumber = "123"
        };
        db.Businesses.Add(new Business { Id = 1, Name = "Test business", IsActive = true });
        var terminal = new PosTerminal
        {
            OutletId = 100,
            Name = "POS 1",
            Code = "POS1",
            IsActive = true,
            IsDefault = true
        };

        db.Outlets.Add(outlet);
        db.Users.Add(new User
        {
            Id = 5,
            BusinessId = 1,
            Name = "Cashier",
            Email = "cashier@example.test",
            PasswordHash = "test",
            OutletId = 100,
            IsActive = true
        });
        db.PosTerminals.Add(terminal);
        db.Inventories.Add(inventory);
        await db.SaveChangesAsync();

        var stockLedger = new Mock<IStockLedgerWriter>();
        var posCache = new Mock<IPosCacheService>();
        var eventPublisher = new Mock<ISaleEventPublisher>();
        var cashSessionService = new Mock<ISaleCashSessionValidator>();
        var invoiceNumberAllocator = new Mock<ISaleInvoiceNumberAllocator>();
        var auditService = new Mock<IAuditService>();

        cashSessionService
            .Setup(x => x.ValidateActiveForSaleAsync(
                100,
                terminal.Id,
                It.IsAny<long?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CashSession
            {
                Id = 200,
                OutletId = 100,
                TerminalId = terminal.Id,
                BusinessDate = DateTime.UtcNow.Date,
                OpeningCashAmount = 0m
            });

        invoiceNumberAllocator.Setup(x => x.AllocateAsync(
                It.IsAny<long>(),
                It.IsAny<long>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestInvoices.Issue("S-1001"));

        var handler = SaleCreationCommandTestFactory.Create(
            db,
            cashSessionService.Object,
            invoiceNumberAllocator.Object,
            auditService.Object,
            stockLedger.Object,
            posCache.Object,
            eventPublisher.Object);

        var result = await SaleCreationCommandTestFactory.ExecuteAsync(handler, new CreateSaleDto
        {
            OutletId = 100,
            CashierId = 5,
            PaymentMethod = "cash",
            IdempotencyKey = "cost-snapshot-1",
            Items = new List<CreateSaleItemDto>
            {
                new() { VariantId = variant.Id, Quantity = 2, UnitPrice = 25m }
            },
            Payments = new List<CreateSalePaymentDto>
            {
                new() { Method = "cash", Amount = 50m, Tendered = 50m }
            }
        });

        var savedItem = await db.SaleItems.SingleAsync();
        Assert.Equal(11.5m, savedItem.HistoricalUnitCost);

        product.CostPrice = 30m;
        variant.CostAdjustment = 5m;
        await db.SaveChangesAsync();

        var sameItemAfterCostChange = await db.SaleItems.SingleAsync();
        Assert.Equal(11.5m, sameItemAfterCostChange.HistoricalUnitCost);
        Assert.Equal("completed", result.Status);
    }

    [Fact]
    public async Task RefundStockLedgerWriter_UsesHistoricalUnitCost_ForFullRefundAndIgnoresCurrentCostChanges()
    {
        var db = CreateDbContext();
        var product = new Product { BusinessId = 1, Name = "Widget", Sku = "W-002", Barcode = "W002", BasePrice = 20m, CostPrice = 50m, Status = ProductStatus.Active, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        var variant = new ProductVariant { BusinessId = 1, Product = product, Name = "Default", Sku = "W-002-DEFAULT", CostAdjustment = 5m, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        var saleItem = new SaleItem { VariantId = variant.Id, Quantity = 3, UnitPrice = 15m, Subtotal = 45m, DiscountAmount = 0 };
        saleItem.CaptureHistoricalUnitCost(11.5m);

        var sale = new Sale
        {
            Id = 77,
            OutletId = 100,
            Status = "completed",
            TotalAmount = 45m,
            Items = new List<SaleItem> { saleItem },
            Payments = new List<SalePayment>()
        };

        db.Products.Add(product);
        db.ProductVariants.Add(variant);
        await db.SaveChangesAsync();
        saleItem.VariantId = variant.Id;
        db.Inventories.Add(new InventoryEntity { VariantId = variant.Id, LocationId = sale.OutletId, LocationType = "outlet", Quantity = 10, LowStockThreshold = 1 });
        await db.SaveChangesAsync();

        var stockLedger = new Mock<IStockLedgerWriter>();

        new SaleRefundStockLedgerWriter(stockLedger.Object).AppendReturn(
            sale,
            saleItem,
            3,
            13,
            5,
            "customer return",
            DateTime.UtcNow);

        stockLedger.Verify(x => x.WriteEntryWithValuation(
            It.IsAny<long>(),
            It.IsAny<long>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<decimal>(),
            It.IsAny<decimal>(),
            It.IsAny<decimal>(),
            It.IsAny<string>(),
            It.IsAny<long>(),
            It.Is<string?>(remarks => remarks != null && remarks.Contains("11.5") && remarks.Contains("3") && remarks.Contains("34.5")),
            It.IsAny<long?>(),
            It.IsAny<DateTime?>(),
            It.IsAny<decimal?>(),
            It.IsAny<decimal?>(),
            It.IsAny<DateTime?>(),
            It.IsAny<long?>(),
            It.IsAny<long?>()), Times.Once);
    }

    [Fact]
    public async Task RefundStockLedgerWriter_UsesHistoricalUnitCost_ForPartialRefundAndIgnoresCurrentCostChanges()
    {
        var db = CreateDbContext();
        var product = new Product { BusinessId = 1, Name = "Widget", Sku = "W-003", Barcode = "W003", BasePrice = 30m, CostPrice = 90m, Status = ProductStatus.Active, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        var variant = new ProductVariant { BusinessId = 1, Product = product, Name = "Default", Sku = "W-003-DEFAULT", CostAdjustment = 6m, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        var saleItem = new SaleItem { VariantId = variant.Id, Quantity = 4, UnitPrice = 20m, Subtotal = 80m, DiscountAmount = 0 };
        saleItem.CaptureHistoricalUnitCost(14m);

        var sale = new Sale { Id = 88, OutletId = 100, Status = "completed", TotalAmount = 80m, Items = new List<SaleItem> { saleItem }, Payments = new List<SalePayment>() };

        db.Products.Add(product);
        db.ProductVariants.Add(variant);
        await db.SaveChangesAsync();
        saleItem.VariantId = variant.Id;
        db.Inventories.Add(new InventoryEntity { VariantId = variant.Id, LocationId = sale.OutletId, LocationType = "outlet", Quantity = 10, LowStockThreshold = 1 });
        await db.SaveChangesAsync();

        var stockLedger = new Mock<IStockLedgerWriter>();

        new SaleRefundStockLedgerWriter(stockLedger.Object).AppendReturn(
            sale,
            saleItem,
            2,
            12,
            5,
            "partial refund",
            DateTime.UtcNow);

        stockLedger.Verify(x => x.WriteEntryWithValuation(
            It.IsAny<long>(),
            It.IsAny<long>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<decimal>(),
            It.IsAny<decimal>(),
            It.IsAny<decimal>(),
            It.IsAny<string>(),
            It.IsAny<long>(),
            It.Is<string?>(remarks => remarks != null && remarks.Contains("14") && remarks.Contains("2") && remarks.Contains("28")),
            It.IsAny<long?>(),
            It.IsAny<DateTime?>(),
            It.IsAny<decimal?>(),
            It.IsAny<decimal?>(),
            It.IsAny<DateTime?>(),
            It.IsAny<long?>(),
            It.IsAny<long?>()), Times.Once);
    }

    [Fact]
    public async Task RefundStockLedgerWriter_UsesHistoricalUnitCost_ForExchangeReturnSideAndIgnoresCurrentCostChanges()
    {
        var db = CreateDbContext();
        var product = new Product { BusinessId = 1, Name = "Widget", Sku = "W-005", Barcode = "W005", BasePrice = 40m, CostPrice = 110m, Status = ProductStatus.Active, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        var variant = new ProductVariant { BusinessId = 1, Product = product, Name = "Default", Sku = "W-005-DEFAULT", CostAdjustment = 7m, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        var saleItem = new SaleItem { VariantId = variant.Id, Quantity = 2, UnitPrice = 30m, Subtotal = 60m, DiscountAmount = 0 };
        saleItem.CaptureHistoricalUnitCost(18m);

        var sale = new Sale { Id = 101, OutletId = 100, Status = "completed", TotalAmount = 60m, Items = new List<SaleItem> { saleItem }, Payments = new List<SalePayment>() };

        db.Products.Add(product);
        db.ProductVariants.Add(variant);
        await db.SaveChangesAsync();
        saleItem.VariantId = variant.Id;
        db.Inventories.Add(new InventoryEntity { VariantId = variant.Id, LocationId = sale.OutletId, LocationType = "outlet", Quantity = 6, LowStockThreshold = 1 });
        await db.SaveChangesAsync();

        var stockLedger = new Mock<IStockLedgerWriter>();

        new SaleRefundStockLedgerWriter(stockLedger.Object).AppendReturn(
            sale,
            saleItem,
            1,
            7,
            5,
            "exchange return",
            DateTime.UtcNow);

        stockLedger.Verify(x => x.WriteEntryWithValuation(
            It.IsAny<long>(),
            It.IsAny<long>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<decimal>(),
            It.IsAny<decimal>(),
            It.IsAny<decimal>(),
            It.IsAny<string>(),
            It.IsAny<long>(),
            It.Is<string?>(remarks => remarks != null && remarks.Contains("18") && remarks.Contains("1") && remarks.Contains("18")),
            It.IsAny<long?>(),
            It.IsAny<DateTime?>(),
            It.IsAny<decimal?>(),
            It.IsAny<decimal?>(),
            It.IsAny<DateTime?>(),
            It.IsAny<long?>(),
            It.IsAny<long?>()), Times.Once);
    }

    [Fact]
    public async Task RefundStockLedgerWriter_ThrowsClearError_WhenHistoricalUnitCostIsMissing()
    {
        var db = CreateDbContext();
        var saleItem = new SaleItem { VariantId = 5, Quantity = 2, UnitPrice = 10m, Subtotal = 20m, DiscountAmount = 0 };
        var sale = new Sale { Id = 102, OutletId = 100, Status = "completed", TotalAmount = 20m, Items = new List<SaleItem> { saleItem }, Payments = new List<SalePayment>() };

        db.Inventories.Add(new InventoryEntity { VariantId = 5, LocationId = sale.OutletId, LocationType = "outlet", Quantity = 5, LowStockThreshold = 1 });
        await db.SaveChangesAsync();

        var stockLedger = new Mock<IStockLedgerWriter>();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new SaleRefundStockLedgerWriter(stockLedger.Object).AppendReturn(
                sale,
                saleItem,
                1,
                6,
                5,
                "legacy return",
                DateTime.UtcNow));
        Assert.Contains("Historical unit cost", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SaleItem_CaptureHistoricalUnitCost_RejectsSecondAssignment()
    {
        var item = new SaleItem();
        item.CaptureHistoricalUnitCost(12.5m);

        var ex = Assert.Throws<InvalidOperationException>(() => item.CaptureHistoricalUnitCost(13m));
        Assert.Contains("historical unit cost", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static RetailPOSDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseInMemoryDatabase($"SaleServiceHistoricalCostSnapshotTests_{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new RetailPOSDbContext(options);
    }
}
