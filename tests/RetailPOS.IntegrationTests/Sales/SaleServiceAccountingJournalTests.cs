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

public class SaleServiceAccountingJournalTests
{
    [Fact]
    public async Task CreateAsync_CommitsJournalIntent_AndTheWorkerPosterProducesTheSaleJournal()
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
            .ReturnsAsync(TestInvoices.Issue("INV-1"));

        var inventoryRow = new RetailPOS.Core.Entities.Inventory { VariantId = 100, LocationId = 11, LocationType = "outlet", Quantity = 10 };
        db.Inventories.Add(inventoryRow);

        var product = new Product { Id = 200, BusinessId = 1, Name = "Widget", CostPrice = 5m, BasePrice = 15m };
        var variant = new ProductVariant { Id = 100, BusinessId = 1, ProductId = 200, Sku = "W1", Name = "Widget", Product = product, CostAdjustment = 0m };
        db.Products.Add(product);
        db.ProductVariants.Add(variant);
        db.SaveChanges();

        var journalSender = SaleJournalCommandTestSender.Create(51, 51);

        var configurationService = new Mock<IAccountingConfigurationService>();
        configurationService.Setup(x => x.ResolvePostingAccountAsync(PostingAccountSlots.Cash, 11)).ReturnsAsync(new PostingAccountResolutionDto { Slot = PostingAccountSlots.Cash, AccountId = 1, Source = "GlobalConfiguration" });
        configurationService.Setup(x => x.ResolvePostingAccountAsync(PostingAccountSlots.Bank, 11)).ReturnsAsync(new PostingAccountResolutionDto { Slot = PostingAccountSlots.Bank, AccountId = 2, Source = "GlobalConfiguration" });
        configurationService.Setup(x => x.ResolvePostingAccountAsync(PostingAccountSlots.Sales, 11)).ReturnsAsync(new PostingAccountResolutionDto { Slot = PostingAccountSlots.Sales, AccountId = 3, Source = "GlobalConfiguration" });
        configurationService.Setup(x => x.ResolvePostingAccountAsync(PostingAccountSlots.SalesDiscount, 11)).ReturnsAsync(new PostingAccountResolutionDto { Slot = PostingAccountSlots.SalesDiscount, AccountId = 4, Source = "GlobalConfiguration" });
        configurationService.Setup(x => x.ResolvePostingAccountAsync(PostingAccountSlots.Cogs, 11)).ReturnsAsync(new PostingAccountResolutionDto { Slot = PostingAccountSlots.Cogs, AccountId = 5, Source = "GlobalConfiguration" });
        configurationService.Setup(x => x.ResolvePostingAccountAsync(PostingAccountSlots.Inventory, 11)).ReturnsAsync(new PostingAccountResolutionDto { Slot = PostingAccountSlots.Inventory, AccountId = 6, Source = "GlobalConfiguration" });
        configurationService.Setup(x => x.ResolvePostingAccountAsync(PostingAccountSlots.RoundingDifference, 11)).ReturnsAsync(new PostingAccountResolutionDto { Slot = PostingAccountSlots.RoundingDifference, AccountId = 7, Source = "GlobalConfiguration" });

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
            OutletId = 11,
            TerminalId = 1,
            CashierId = 5,
            PaymentMethod = "cash",
            IdempotencyKey = "journal-sale-1",
            Payments = new List<CreateSalePaymentDto> { new() { Method = "cash", Amount = 15m, Tendered = 15m } },
            Items = new List<CreateSaleItemDto> { new() { VariantId = 100, Quantity = 1, UnitPrice = 15m, DiscountAmount = 0m } }
        });

        Assert.Equal("completed", result.Status);
        // D10: checkout commits a pending intent and posts nothing to the General Ledger itself.
        Assert.Empty(journalSender.Invocations);
        var intent = Assert.Single(db.SaleFinancialPostings.AsNoTracking());
        Assert.Equal(result.Id, intent.SaleId);
        Assert.Equal(SaleFinancialPostingStatuses.Pending, intent.Status);
        Assert.Equal($"sale:{result.Id}:posting:v1", intent.SourceKey);

        var saleAudit = Assert.Single(auditEvents, x => x.PrimaryEntity?.Type == "Sale" && x.PrimaryEntity?.Id == result.Id.ToString() && x.ActionType == AuditActionType.Create);
        Assert.Null(saleAudit.Metadata?["journalEntryId"]);
        Assert.Equal(5L, saleAudit.Metadata?["actorUserId"]);

        var posted = await SaleCreationCommandTestFactory.PostSaleIntentAsync(
            db, result.Id, journalSender.Object, configurationService.Object);
        Assert.Equal(51L, posted.JournalEntryId);
        var draftCommand = Assert.IsType<CreateOrGetSystemJournalDraftCommand>(
            journalSender.Invocations.Single(invocation =>
                invocation.Method.Name == nameof(ISender.Send)
                && invocation.Arguments[0] is CreateOrGetSystemJournalDraftCommand).Arguments[0]);
        Assert.Equal("SalePosting", draftCommand.Journal.JournalType);
        Assert.Equal(5, draftCommand.ActorUserId);
        Assert.Equal($"sale:{result.Id}:posting:v1", draftCommand.IdempotencyDiscriminator);
        Assert.Equal("Sale", draftCommand.Journal.ReferenceType);
        Assert.Equal(result.Id.ToString(), draftCommand.Journal.ReferenceId);
        Assert.Contains(draftCommand.Journal.Lines, line => line.AccountId == 1 && line.DebitAmount == 15m);
        Assert.Contains(draftCommand.Journal.Lines, line => line.AccountId == 3 && line.CreditAmount == 15m);
        Assert.Equal(1L, posted.PaymentAccounts.Values.Single());
    }

    private static RetailPOSDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseInMemoryDatabase($"SaleServiceAccountingJournalTests_{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new RetailPOSDbContext(options);
    }
}
