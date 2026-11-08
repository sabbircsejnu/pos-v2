using System.Reflection;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using RetailPOS.API.DTOs.Accounting;
using RetailPOS.API.DTOs.Sale;
using RetailPOS.API.Services;
using RetailPOS.Application.AccountingJournals.Commands;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Application.Sales.FinancialPostings;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Tests.Sales;

public class SaleServiceVatAndExchangeAccountingTests
{
    [Fact]
    public async Task CreateAsync_JournalIntentPostsVatPayableLine_ForTaxableSale()
    {
        var db = CreateDbContext();
        var outlet = new Outlet { Id = 11, BusinessId = 1, Name = "Main" };
        var cashSession = new CashSession { Id = 21, OutletId = 11, BusinessId = 1, TerminalId = 1, BusinessDate = DateTime.UtcNow.Date, Status = "open", OpenedByUserId = 7 };
        var terminal = new PosTerminal { Id = 1, OutletId = 11, IsActive = true, IsDefault = true, Name = "T1" };
        var business = new Business { Id = 1, Name = "Test Business" };
        db.Businesses.Add(business);
        db.Outlets.Add(outlet);
        db.Users.Add(new User
        {
            Id = 5,
            BusinessId = 1,
            Name = "Cashier",
            Email = "cashier@example.test",
            PasswordHash = "test",
            OutletId = 11,
            IsActive = true
        });
        db.PosTerminals.Add(terminal);
        db.CashSessions.Add(cashSession);
        db.SaveChanges();

        var inventory = new RetailPOS.Core.Entities.Inventory { VariantId = 100, LocationId = 11, LocationType = "outlet", Quantity = 10 };
        var product = new Product { Id = 200, BusinessId = 1, Name = "Widget", CostPrice = 5m, BasePrice = 100m };
        var variant = new ProductVariant { Id = 100, BusinessId = 1, ProductId = 200, Sku = "W1", Name = "Widget", Product = product, CostAdjustment = 0m };
        db.Inventories.Add(inventory);
        db.Products.Add(product);
        db.ProductVariants.Add(variant);
        db.SaveChanges();

        var stockLedger = new Mock<IStockLedgerWriter>();
        var posCache = new Mock<IPosCacheService>();
        var eventPublisher = new Mock<ISaleEventPublisher>();
        var cashSessionService = new Mock<ISaleCashSessionValidator>();
        var invoiceNumberAllocator = new Mock<ISaleInvoiceNumberAllocator>();
        var auditService = new Mock<IAuditService>();
        var auditEvents = new List<AuditEventInput>();
        auditService
            .Setup(x => x.RecordAsync(It.IsAny<AuditEventInput>(), It.IsAny<CancellationToken>()))
            .Callback<AuditEventInput, CancellationToken>((input, _) => auditEvents.Add(input))
            .Returns(Task.CompletedTask);
        cashSessionService
            .Setup(x => x.ValidateActiveForSaleAsync(11, 1, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cashSession);

        invoiceNumberAllocator
            .Setup(x => x.AllocateAsync(It.IsAny<long>(), It.IsAny<long>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestInvoices.Issue("INV-2"));

        var journalSender = SaleJournalCommandTestSender.Create(71, 71);

        var configurationService = new Mock<IAccountingConfigurationService>();
        configurationService.Setup(x => x.ResolvePostingAccountAsync(PostingAccountSlots.Cash, 11)).ReturnsAsync(new PostingAccountResolutionDto { Slot = PostingAccountSlots.Cash, AccountId = 1, Source = "GlobalConfiguration" });
        configurationService.Setup(x => x.ResolvePostingAccountAsync(PostingAccountSlots.Bank, 11)).ReturnsAsync(new PostingAccountResolutionDto { Slot = PostingAccountSlots.Bank, AccountId = 2, Source = "GlobalConfiguration" });
        configurationService.Setup(x => x.ResolvePostingAccountAsync(PostingAccountSlots.Sales, 11)).ReturnsAsync(new PostingAccountResolutionDto { Slot = PostingAccountSlots.Sales, AccountId = 3, Source = "GlobalConfiguration" });
        configurationService.Setup(x => x.ResolvePostingAccountAsync(PostingAccountSlots.SalesDiscount, 11)).ReturnsAsync(new PostingAccountResolutionDto { Slot = PostingAccountSlots.SalesDiscount, AccountId = 4, Source = "GlobalConfiguration" });
        configurationService.Setup(x => x.ResolvePostingAccountAsync(PostingAccountSlots.Cogs, 11)).ReturnsAsync(new PostingAccountResolutionDto { Slot = PostingAccountSlots.Cogs, AccountId = 5, Source = "GlobalConfiguration" });
        configurationService.Setup(x => x.ResolvePostingAccountAsync(PostingAccountSlots.Inventory, 11)).ReturnsAsync(new PostingAccountResolutionDto { Slot = PostingAccountSlots.Inventory, AccountId = 6, Source = "GlobalConfiguration" });
        configurationService.Setup(x => x.ResolvePostingAccountAsync(PostingAccountSlots.RoundingDifference, 11)).ReturnsAsync(new PostingAccountResolutionDto { Slot = PostingAccountSlots.RoundingDifference, AccountId = 7, Source = "GlobalConfiguration" });
        configurationService.Setup(x => x.ResolvePostingAccountAsync("vat_payable", 11)).ReturnsAsync(new PostingAccountResolutionDto { Slot = "vat_payable", AccountId = 8, Source = "GlobalConfiguration" });

        var handler = SaleCreationCommandTestFactory.Create(
            db,
            cashSessionService.Object,
            invoiceNumberAllocator.Object,
            auditService.Object,
            stockLedger.Object,
            posCache.Object,
            eventPublisher.Object,
            SaleCreationCommandTestFactory.TaxPolicy(20m));

        var result = await SaleCreationCommandTestFactory.ExecuteAsync(handler, new CreateSaleDto
        {
            OutletId = 11,
            TerminalId = 1,
            CashierId = 5,
            PaymentMethod = "cash",
            IdempotencyKey = "vat-sale-1",
            Tax = 20m,
            Payments = new List<CreateSalePaymentDto> { new() { Method = "cash", Amount = 120m, Tendered = 120m } },
            Items = new List<CreateSaleItemDto> { new() { VariantId = 100, Quantity = 1, UnitPrice = 100m, DiscountAmount = 0m } }
        });

        Assert.Equal("completed", result.Status);
        Assert.Empty(journalSender.Invocations);
        await SaleCreationCommandTestFactory.PostSaleIntentAsync(
            db, result.Id, journalSender.Object, configurationService.Object);
        var draftCommand = Assert.IsType<CreateOrGetSystemJournalDraftCommand>(
            journalSender.Invocations.Single(invocation =>
                invocation.Method.Name == nameof(ISender.Send)
                && invocation.Arguments[0] is CreateOrGetSystemJournalDraftCommand).Arguments[0]);
        Assert.Contains(draftCommand.Journal.Lines, line => line.AccountId == 8 && line.CreditAmount == 20m);
        Assert.Equal($"sale:{result.Id}:posting:v1", draftCommand.IdempotencyDiscriminator);
    }

    [Fact]
    public async Task SaleReversalAccountingPoster_PostsVatReversal_ForPartialRefundOnly()
    {
        var db = CreateDbContext();
        var sale = new Sale
        {
            Id = 77,
            OutletId = 11,
            CashierId = 5,
            Status = "completed",
            TotalAmount = 120m,
            Tax = 20m,
            Discount = 0m,
            Items = new List<SaleItem> { new() { VariantId = 100, Quantity = 4, UnitPrice = 25m, Subtotal = 100m, DiscountAmount = 0m, TaxableAmount = 100m, VatAmount = 20m } },
            Payments = new List<SalePayment> { new() { Method = "cash", Amount = 120m } }
        };
        sale.Items.First().CaptureHistoricalUnitCost(10m);

        var product = new Product { Id = 200, BusinessId = 1, Name = "Widget", CostPrice = 5m };
        var variant = new ProductVariant { Id = 100, BusinessId = 1, ProductId = 200, Sku = "W1", Name = "Widget", Product = product, CostAdjustment = 0m };
        db.Products.Add(product);
        db.ProductVariants.Add(variant);
        db.Inventories.Add(new RetailPOS.Core.Entities.Inventory { VariantId = 100, LocationId = 11, LocationType = "outlet", Quantity = 10 });
        await db.SaveChangesAsync();

        var stockLedger = new Mock<IStockLedgerWriter>();
        var posCache = new Mock<IPosCacheService>();
        var eventPublisher = new Mock<ISaleEventPublisher>();
        var cashSessionService = new Mock<ISaleCashSessionValidator>();
        var invoiceNumberAllocator = new Mock<ISaleInvoiceNumberAllocator>();
        var auditService = new Mock<IAuditService>();
        var auditEvents = new List<AuditEventInput>();
        auditService
            .Setup(x => x.RecordAsync(It.IsAny<AuditEventInput>(), It.IsAny<CancellationToken>()))
            .Callback<AuditEventInput, CancellationToken>((input, _) => auditEvents.Add(input))
            .Returns(Task.CompletedTask);
        var journalSender = SaleJournalCommandTestSender.Create(72, 72);

        var configurationService = new Mock<IAccountingConfigurationService>();
        configurationService.Setup(x => x.ResolvePostingAccountAsync(PostingAccountSlots.SalesReturn, 11)).ReturnsAsync(new PostingAccountResolutionDto { Slot = PostingAccountSlots.SalesReturn, AccountId = 9, Source = "GlobalConfiguration" });
        configurationService.Setup(x => x.ResolvePostingAccountAsync(PostingAccountSlots.Cogs, 11)).ReturnsAsync(new PostingAccountResolutionDto { Slot = PostingAccountSlots.Cogs, AccountId = 5, Source = "GlobalConfiguration" });
        configurationService.Setup(x => x.ResolvePostingAccountAsync(PostingAccountSlots.Inventory, 11)).ReturnsAsync(new PostingAccountResolutionDto { Slot = PostingAccountSlots.Inventory, AccountId = 6, Source = "GlobalConfiguration" });
        configurationService.Setup(x => x.ResolvePostingAccountAsync(PostingAccountSlots.RoundingDifference, 11)).ReturnsAsync(new PostingAccountResolutionDto { Slot = PostingAccountSlots.RoundingDifference, AccountId = 7, Source = "GlobalConfiguration" });
        configurationService.Setup(x => x.ResolvePostingAccountAsync(PostingAccountSlots.Cash, 11)).ReturnsAsync(new PostingAccountResolutionDto { Slot = PostingAccountSlots.Cash, AccountId = 1, Source = "GlobalConfiguration" });
        configurationService.Setup(x => x.ResolvePostingAccountAsync(PostingAccountSlots.Bank, 11)).ReturnsAsync(new PostingAccountResolutionDto { Slot = PostingAccountSlots.Bank, AccountId = 2, Source = "GlobalConfiguration" });
        configurationService.Setup(x => x.ResolvePostingAccountAsync("vat_payable", 11)).ReturnsAsync(new PostingAccountResolutionDto { Slot = "vat_payable", AccountId = 8, Source = "GlobalConfiguration" });

        sale.BusinessId = 1;
        var intent = SaleFinancialIntentFactory.ForReversal(
            sale, 1, [new SaleRefundLine(sale.Items.Single().Id, 100, 2)],
            SaleFinancialPostingReversalTypes.Refund, 99, DateTime.UtcNow);
        await new SaleFinancialJournalPoster(journalSender.Object, configurationService.Object)
            .PostReversalAsync(
                SaleFinancialIntentFactory.ReadReversalSnapshot(intent),
                new Dictionary<long, long?>(),
                CancellationToken.None);

        var refundDraft = Assert.IsType<CreateOrGetSystemJournalDraftCommand>(
            journalSender.Invocations.Single(invocation =>
                invocation.Method.Name == nameof(ISender.Send)
                && invocation.Arguments[0] is CreateOrGetSystemJournalDraftCommand).Arguments[0]);
        Assert.Contains(refundDraft.Journal.Lines, line => line.AccountId == 8 && line.DebitAmount == 10m);
        Assert.Equal($"sale:{sale.Id}:reversal:v1", refundDraft.IdempotencyDiscriminator);
        Assert.Equal(99, refundDraft.ActorUserId);
    }

    [Fact]
    public async Task CreateAsync_RejectsNewExchangeLinkage_AndLeavesTheOriginalUntouched()
    {
        var db = CreateDbContext();
        var originalSale = new Sale { Id = 77, OutletId = 11, Status = "completed", TotalAmount = 100m, Items = new List<SaleItem>(), Payments = new List<SalePayment>() };
        db.Sales.Add(originalSale);
        await db.SaveChangesAsync();

        var outlet = new Outlet { Id = 11, BusinessId = 1, Name = "Main" };
        var business = new Business { Id = 1, Name = "Test Business" };
        var cashSession = new CashSession { Id = 21, OutletId = 11, BusinessId = 1, TerminalId = 1, BusinessDate = DateTime.UtcNow.Date, Status = "open", OpenedByUserId = 7 };
        var terminal = new PosTerminal { Id = 1, OutletId = 11, IsActive = true, IsDefault = true, Name = "T1" };
        db.Businesses.Add(business);
        db.Outlets.Add(outlet);
        db.Users.Add(new User
        {
            Id = 5,
            BusinessId = 1,
            Name = "Cashier",
            Email = "cashier@example.test",
            PasswordHash = "test",
            OutletId = 11,
            IsActive = true
        });
        db.PosTerminals.Add(terminal);
        db.CashSessions.Add(cashSession);

        var inventory = new RetailPOS.Core.Entities.Inventory { VariantId = 100, LocationId = 11, LocationType = "outlet", Quantity = 10 };
        var product = new Product { Id = 200, BusinessId = 1, Name = "Widget", CostPrice = 5m, BasePrice = 100m };
        var variant = new ProductVariant { Id = 100, BusinessId = 1, ProductId = 200, Sku = "W1", Name = "Widget", Product = product, CostAdjustment = 0m };
        db.Inventories.Add(inventory);
        db.Products.Add(product);
        db.ProductVariants.Add(variant);
        db.SaveChanges();

        var stockLedger = new Mock<IStockLedgerWriter>();
        var posCache = new Mock<IPosCacheService>();
        var eventPublisher = new Mock<ISaleEventPublisher>();
        var cashSessionService = new Mock<ISaleCashSessionValidator>();
        var invoiceNumberAllocator = new Mock<ISaleInvoiceNumberAllocator>();
        var auditService = new Mock<IAuditService>();
        var auditEvents = new List<AuditEventInput>();
        auditService
            .Setup(x => x.RecordAsync(It.IsAny<AuditEventInput>(), It.IsAny<CancellationToken>()))
            .Callback<AuditEventInput, CancellationToken>((input, _) => auditEvents.Add(input))
            .Returns(Task.CompletedTask);
        cashSessionService
            .Setup(x => x.ValidateActiveForSaleAsync(11, 1, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cashSession);

        invoiceNumberAllocator
            .Setup(x => x.AllocateAsync(It.IsAny<long>(), It.IsAny<long>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestInvoices.Issue("INV-3"));

        var handler = SaleCreationCommandTestFactory.Create(
            db,
            cashSessionService.Object,
            invoiceNumberAllocator.Object,
            auditService.Object,
            stockLedger.Object,
            posCache.Object,
            eventPublisher.Object);

        // Exchange D-01: exchanges are dedicated Exchange transactions; checkout refuses a new link before any effect.
        var error = await Assert.ThrowsAsync<RetailPOS.Application.Sales.Checkout.SaleCheckoutValidationException>(() =>
            SaleCreationCommandTestFactory.ExecuteAsync(handler, new CreateSaleDto
            {
                OutletId = 11,
                TerminalId = 1,
                CashierId = 5,
                PaymentMethod = "cash",
                Payments = new List<CreateSalePaymentDto> { new() { Method = "cash", Amount = 100m, Tendered = 100m } },
                Items = new List<CreateSaleItemDto> { new() { VariantId = 100, Quantity = 1, UnitPrice = 100m, DiscountAmount = 0m } },
                OriginalSaleId = 77,
                IdempotencyKey = "exchange-1"
            }));
        Assert.Equal("SALE_EXCHANGE_LINK_DEPRECATED", error.Code);

        var originalSaleFromDb = await db.Sales.FindAsync(77L);
        Assert.Null(GetPropertyValue<long?>(originalSaleFromDb!, "ReplacementSaleId"));
        Assert.Equal(1, await db.Sales.CountAsync());
        Assert.DoesNotContain(auditEvents, x => x.PrimaryEntity?.Type == "Sale" && x.ActionType == AuditActionType.Create);
    }

    private static T? GetPropertyValue<T>(object instance, string propertyName)
    {
        var prop = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
        return prop == null ? default : (T?)prop.GetValue(instance);
    }

    private static RetailPOSDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseInMemoryDatabase($"SaleServiceVatAndExchangeAccountingTests_{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new RetailPOSDbContext(options);
    }

}
