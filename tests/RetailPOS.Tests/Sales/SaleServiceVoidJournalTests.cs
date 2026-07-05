using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Moq;
using RetailPOS.API.DTOs.Sale;
using RetailPOS.API.Services;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Repositories;

namespace RetailPOS.Tests.Sales;

public class SaleServiceVoidJournalTests
{
    [Fact]
    public async Task VoidAsync_WritesSaleVoidJournal_WithActorAndReason()
    {
        var db = CreateDbContext();

        var saleRepo = new Mock<ISaleRepository>();
        var customerRepo = new Mock<ICustomerRepository>();
        var stockLedger = new Mock<IStockLedgerService>();
        var posCache = new Mock<IPosCacheService>();
        var eventPublisher = new Mock<ISaleEventPublisher>();
        var settingsService = new Mock<ISettingsService>();
        var logger = Mock.Of<ILogger<SaleService>>();

        saleRepo.Setup(r => r.GetByIdAsync(77)).ReturnsAsync(new Sale
        {
            Id = 77,
            OutletId = 11,
            Status = "completed",
            TotalAmount = 120m,
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

        var result = await service.VoidAsync(77, new VoidSaleDto { Reason = "  policy override  " }, 99);

        Assert.Equal("voided", result.Status);

        var journal = db.SaleVoids.Single();
        Assert.Equal(77, journal.SaleId);
        Assert.Equal(99, journal.VoidedByUserId);
        Assert.Equal("completed", journal.PreviousStatus);
        Assert.Equal("policy override", journal.Reason);
    }

    [Fact]
    public async Task VoidAsync_DoesNotWriteJournal_WhenSaleNotVoidable()
    {
        var db = CreateDbContext();

        var saleRepo = new Mock<ISaleRepository>();
        var customerRepo = new Mock<ICustomerRepository>();
        var stockLedger = new Mock<IStockLedgerService>();
        var posCache = new Mock<IPosCacheService>();
        var eventPublisher = new Mock<ISaleEventPublisher>();
        var settingsService = new Mock<ISettingsService>();
        var logger = Mock.Of<ILogger<SaleService>>();

        saleRepo.Setup(r => r.GetByIdAsync(88)).ReturnsAsync(new Sale
        {
            Id = 88,
            OutletId = 11,
            Status = "voided",
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

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.VoidAsync(88, new VoidSaleDto { Reason = "x" }, 9));
        Assert.Empty(db.SaleVoids);
    }

    private static RetailPOSDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseInMemoryDatabase($"SaleServiceVoidJournalTests_{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new RetailPOSDbContext(options);
    }
}
