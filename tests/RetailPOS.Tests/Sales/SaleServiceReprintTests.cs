using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using RetailPOS.API.Services;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Repositories;

namespace RetailPOS.Tests.Sales;

public class SaleServiceReprintTests
{
    [Fact]
    public async Task ReprintReceiptAsync_ReturnsMappedSale_ForCompletedSale()
    {
        var db = CreateDbContext();

        var saleRepo = new Mock<ISaleRepository>();
        var customerRepo = new Mock<ICustomerRepository>();
        var stockLedger = new Mock<IStockLedgerService>();
        var posCache = new Mock<IPosCacheService>();
        var eventPublisher = new Mock<ISaleEventPublisher>();
        var settingsService = new Mock<ISettingsService>();
        var logger = Mock.Of<ILogger<SaleService>>();

        saleRepo.Setup(r => r.GetByIdAsync(42)).ReturnsAsync(new Sale
        {
            Id = 42,
            SaleNumber = "S-20260705-10001",
            OutletId = 11,
            CashierId = 7,
            Status = "completed",
            TotalAmount = 100m,
            Discount = 0m,
            Tax = 0m,
            PaymentMethod = "cash",
            Items = new List<SaleItem>(),
            Payments = new List<SalePayment>()
        });

        var service = new SaleService(
            saleRepo.Object,
            customerRepo.Object,
            db,
            logger,
            stockLedger.Object,
            posCache.Object,
            eventPublisher.Object,
            settingsService.Object);

        var result = await service.ReprintReceiptAsync(42);

        Assert.Equal(42, result.Id);
        Assert.Equal("S-20260705-10001", result.SaleNumber);
        Assert.Equal("completed", result.Status);

        var history = db.ReceiptPrintHistories.Single();
        Assert.Equal(42, history.SaleId);
        Assert.Equal("reprint", history.ActionType);
    }

    [Fact]
    public async Task ReprintReceiptAsync_Throws_ForNonFinalizedSale()
    {
        var db = CreateDbContext();

        var saleRepo = new Mock<ISaleRepository>();
        var customerRepo = new Mock<ICustomerRepository>();
        var stockLedger = new Mock<IStockLedgerService>();
        var posCache = new Mock<IPosCacheService>();
        var eventPublisher = new Mock<ISaleEventPublisher>();
        var settingsService = new Mock<ISettingsService>();
        var logger = Mock.Of<ILogger<SaleService>>();

        saleRepo.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(new Sale
        {
            Id = 5,
            SaleNumber = "S-20260705-10002",
            OutletId = 11,
            CashierId = 7,
            Status = "draft",
            TotalAmount = 20m,
            PaymentMethod = "cash",
            Items = new List<SaleItem>(),
            Payments = new List<SalePayment>()
        });

        var service = new SaleService(
            saleRepo.Object,
            customerRepo.Object,
            db,
            logger,
            stockLedger.Object,
            posCache.Object,
            eventPublisher.Object,
            settingsService.Object);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReprintReceiptAsync(5));
        Assert.Contains("Only finalized sales can be reprinted", ex.Message);

        Assert.Empty(db.ReceiptPrintHistories);
    }

    private static RetailPOSDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseInMemoryDatabase($"SaleServiceReprintTests_{Guid.NewGuid()}")
            .Options;

        return new RetailPOSDbContext(options);
    }
}
