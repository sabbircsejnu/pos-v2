using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using RetailPOS.API.DTOs.Sale;
using RetailPOS.API.Services;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Application.Sales.Checkout;
using RetailPOS.Application.Sales.Commands;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.Data;
using InventoryEntity = RetailPOS.Core.Entities.Inventory;

namespace RetailPOS.Tests.Sales;

/// <summary>P1 checkout hardening: the server, not the client, decides price, tax, totals, payment evidence and replay behaviour.</summary>
public sealed class SaleCheckoutAuthorityTests
{
    private const long OutletId = 11;
    private const long CashierId = 5;

    [Fact]
    public async Task StalePrice_IsRejectedWithTheCurrentPriceAndNothingIsWritten()
    {
        using var scenario = Scenario.Create();

        var exception = await Assert.ThrowsAsync<SaleCheckoutConflictException>(() =>
            scenario.CompleteAsync(Request("stale-price", unitPrice: 90m, cashTendered: 200m)));

        Assert.Equal(SaleCheckoutErrorCodes.PriceChanged, exception.Code);
        var change = Assert.Single(Assert.IsType<SalePriceChange[]>(exception.Details));
        Assert.Equal(100m, change.CurrentUnitPrice);
        Assert.Equal(90m, change.SubmittedUnitPrice);
        scenario.AssertNothingWritten();
    }

    [Fact]
    public async Task PricingRuleDiscount_IsAppliedByTheServerAndRecordedOnTheLine()
    {
        using var scenario = Scenario.Create();
        scenario.Db.PriceRules.Add(new PriceRule
        {
            BusinessId = 1, Name = "10% off", RuleType = "global", DiscountType = "percentage", DiscountValue = 10m, IsActive = true
        });
        await scenario.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<SaleCheckoutConflictException>(() =>
            scenario.CompleteAsync(Request("rule-stale", unitPrice: 100m, cashTendered: 200m)));

        var sale = await scenario.CompleteAsync(Request("rule-ok", unitPrice: 90m, cashTendered: 200m));

        var item = Assert.Single(sale.Items);
        Assert.Equal(90m, item.UnitPrice);
        Assert.Equal("10% off", item.AppliedRuleName);
        Assert.Equal(180m, sale.TotalAmount);
    }

    [Fact]
    public async Task Tax_IsCalculatedByTheServerAndAForgedValueIsRejected()
    {
        using var scenario = Scenario.Create(taxRate: 10m);

        var forged = await Assert.ThrowsAsync<SaleCheckoutConflictException>(() =>
            scenario.CompleteAsync(Request("tax-forged", tax: 0m, cashTendered: 220m)));
        Assert.Equal(SaleCheckoutErrorCodes.TaxMismatch, forged.Code);
        scenario.AssertNothingWritten();

        var sale = await scenario.CompleteAsync(Request("tax-ok", tax: 20m, cashTendered: 220m));

        Assert.Equal(20m, sale.Tax);
        Assert.Equal(220m, sale.TotalAmount);
    }

    [Fact]
    public async Task PersistedLineVat_AddsUpToTheSaleTaxWhenAnInvoiceDiscountExists()
    {
        using var scenario = Scenario.Create(taxRate: 10m);

        // 100 + 50 = 150, invoice discount 15 -> taxable 135, VAT 13.50, payable 148.50.
        var sale = await scenario.CompleteAsync(new CreateSaleDto
        {
            OutletId = OutletId, TerminalId = 1, CashierId = CashierId, IdempotencyKey = "vat-allocation", PaymentMethod = "cash",
            Discount = 15m, Tax = 13.50m, CashTendered = 150m,
            Items =
            [
                new() { VariantId = 100, Quantity = 1, UnitPrice = 100m },
                new() { VariantId = 101, Quantity = 1, UnitPrice = 50m }
            ]
        });

        Assert.Equal(148.50m, sale.TotalAmount);
        var saved = await scenario.Db.Sales.Include(item => item.Items).SingleAsync(item => item.Id == sale.Id);
        Assert.Equal(saved.Tax, saved.Items.Sum(item => item.VatAmount));
        Assert.Equal(135m, saved.Items.Sum(item => item.TaxableAmount));
        Assert.All(saved.Items, item => Assert.Equal(10m, item.VatRate));
    }

    [Fact]
    public async Task LineAndInvoiceDiscounts_CannotBeStacked_AndDiscountsCannotExceedTheAmount()
    {
        using var scenario = Scenario.Create();

        var stacked = Request("discount-stack", cashTendered: 500m);
        stacked.Discount = 5m;
        stacked.Items[0].DiscountAmount = 5m;
        var conflict = await Assert.ThrowsAsync<SaleCheckoutValidationException>(() => scenario.CompleteAsync(stacked));
        Assert.Equal(SaleCheckoutErrorCodes.DiscountConflict, conflict.Code);

        var excessive = Request("discount-excess", cashTendered: 500m);
        excessive.Items[0].DiscountAmount = 200.01m;
        var invalid = await Assert.ThrowsAsync<SaleCheckoutValidationException>(() => scenario.CompleteAsync(excessive));
        Assert.Equal(SaleCheckoutErrorCodes.InvalidDiscount, invalid.Code);
        scenario.AssertNothingWritten();
    }

    [Fact]
    public async Task CashPayment_PersistsTenderChangeAndRetainedCash()
    {
        using var scenario = Scenario.Create();

        var sale = await scenario.CompleteAsync(Request("cash-evidence", cashTendered: 250m));

        var payment = Assert.Single(await scenario.Db.SalePayments.ToListAsync());
        Assert.Equal("cash", payment.Method);
        Assert.Equal(200m, payment.Amount);
        Assert.Equal(250m, payment.Tendered);
        Assert.Equal(50m, payment.ChangeGiven);
        Assert.Equal(200m, payment.NetCashRetained);
        Assert.Equal(50m, Assert.Single(sale.Payments).Change);
    }

    [Fact]
    public async Task CashPayment_WithoutACoveringTender_IsRejected()
    {
        using var scenario = Scenario.Create();

        var missing = await Assert.ThrowsAsync<SaleCheckoutValidationException>(() =>
            scenario.CompleteAsync(Request("cash-missing", cashTendered: null)));
        var short_ = await Assert.ThrowsAsync<SaleCheckoutValidationException>(() =>
            scenario.CompleteAsync(Request("cash-short", cashTendered: 199.99m)));

        Assert.Equal(SaleCheckoutErrorCodes.InvalidPayment, missing.Code);
        Assert.Equal(SaleCheckoutErrorCodes.InvalidPayment, short_.Code);
        scenario.AssertNothingWritten();
    }

    [Fact]
    public async Task SplitPayments_MustMatchTheServerTotalExactly()
    {
        using var scenario = Scenario.Create();
        var underpaid = Request("split-under", cashTendered: null);
        underpaid.Payments =
        [
            new() { Method = "cash", Amount = 100m, Tendered = 100m },
            new() { Method = "card", Amount = 99.99m }
        ];

        var mismatch = await Assert.ThrowsAsync<SaleCheckoutValidationException>(() => scenario.CompleteAsync(underpaid));
        Assert.Equal(SaleCheckoutErrorCodes.PaymentMismatch, mismatch.Code);

        var exact = Request("split-ok", cashTendered: null);
        exact.Payments =
        [
            new() { Method = "cash", Amount = 120m, Tendered = 150m },
            new() { Method = "mobile", Amount = 80m }
        ];
        var sale = await scenario.CompleteAsync(exact);

        Assert.Equal(2, sale.Payments.Count);
        var cash = await scenario.Db.SalePayments.SingleAsync(payment => payment.Method == "cash");
        Assert.Equal(30m, cash.ChangeGiven);
        Assert.Equal(120m, cash.NetCashRetained);
        var mobile = await scenario.Db.SalePayments.SingleAsync(payment => payment.Method == "mobile");
        Assert.Null(mobile.Tendered);
        Assert.Null(mobile.ChangeGiven);
    }

    [Fact]
    public async Task UnsupportedPaymentMethod_IsRejected()
    {
        using var scenario = Scenario.Create();
        var request = Request("cheque", cashTendered: null);
        request.PaymentMethod = "cheque";

        var exception = await Assert.ThrowsAsync<SaleCheckoutValidationException>(() => scenario.CompleteAsync(request));

        Assert.Equal(SaleCheckoutErrorCodes.InvalidPayment, exception.Code);
    }

    [Fact]
    public async Task IdempotencyKey_IsRequired()
    {
        using var scenario = Scenario.Create();

        var exception = await Assert.ThrowsAsync<SaleCheckoutValidationException>(() =>
            scenario.CompleteAsync(Request(" ", cashTendered: 200m)));

        Assert.Equal(SaleCheckoutErrorCodes.IdempotencyKeyRequired, exception.Code);
    }

    [Fact]
    public async Task Replay_ReturnsTheOriginalSale_AndAChangedPayloadIsAConflict()
    {
        using var scenario = Scenario.Create();
        var first = await scenario.CompleteAsync(Request("replay", cashTendered: 200m));
        var fingerprint = (await scenario.Db.Sales.SingleAsync(item => item.Id == first.Id)).IdempotencyFingerprint;
        Assert.Equal(64, fingerprint?.Length);

        var replay = await scenario.CompleteAsync(Request("replay", cashTendered: 200m));
        Assert.Equal(first.Id, replay.Id);

        var different = Request("replay", cashTendered: 200m);
        different.Items[0].Quantity = 1;
        different.Tax = 0m;
        var conflict = await Assert.ThrowsAsync<SaleCheckoutConflictException>(() => scenario.CompleteAsync(different));
        Assert.Equal(SaleCheckoutErrorCodes.IdempotencyPayloadMismatch, conflict.Code);

        Assert.Single(scenario.NewSales());
        Assert.Equal(8, (await scenario.Db.Inventories.SingleAsync(item => item.VariantId == 100)).Quantity);
    }

    [Fact]
    public async Task InactiveProducts_CannotBeSold()
    {
        using var scenario = Scenario.Create();
        var request = Request("inactive", cashTendered: 500m);
        request.Items[0].VariantId = 102;

        var exception = await Assert.ThrowsAsync<SaleCheckoutValidationException>(() => scenario.CompleteAsync(request));

        Assert.Equal(SaleCheckoutErrorCodes.VariantNotSellable, exception.Code);
    }

    [Fact]
    public async Task ExchangeLink_MustReferenceACompletedUnreplacedSaleOfTheSameBusiness()
    {
        using var scenario = Scenario.Create();

        var crossBusiness = Request("exchange-other-business", cashTendered: 200m);
        crossBusiness.OriginalSaleId = 90;
        await Assert.ThrowsAsync<KeyNotFoundException>(() => scenario.CompleteAsync(crossBusiness));

        var missing = Request("exchange-missing", cashTendered: 200m);
        missing.OriginalSaleId = 9999;
        await Assert.ThrowsAsync<KeyNotFoundException>(() => scenario.CompleteAsync(missing));

        var voided = Request("exchange-voided", cashTendered: 200m);
        voided.OriginalSaleId = 91;
        Assert.Equal(
            SaleCheckoutErrorCodes.InvalidExchangeLink,
            (await Assert.ThrowsAsync<SaleCheckoutValidationException>(() => scenario.CompleteAsync(voided))).Code);

        var replaced = Request("exchange-replaced", cashTendered: 200m);
        replaced.OriginalSaleId = 92;
        Assert.Equal(
            SaleCheckoutErrorCodes.InvalidExchangeLink,
            (await Assert.ThrowsAsync<SaleCheckoutValidationException>(() => scenario.CompleteAsync(replaced))).Code);

        var valid = Request("exchange-valid", cashTendered: 200m);
        valid.OriginalSaleId = 93;
        var sale = await scenario.CompleteAsync(valid);
        Assert.Equal(sale.Id, (await scenario.Db.Sales.FindAsync(93L))!.ReplacementSaleId);

        // The same original can only be replaced once.
        var second = Request("exchange-second", cashTendered: 200m);
        second.OriginalSaleId = 93;
        await Assert.ThrowsAsync<SaleCheckoutValidationException>(() => scenario.CompleteAsync(second));
    }

    private static CreateSaleDto Request(
        string key,
        decimal unitPrice = 100m,
        decimal tax = 0m,
        decimal? cashTendered = null) => new()
    {
        OutletId = OutletId,
        TerminalId = 1,
        CashierId = CashierId,
        PaymentMethod = "cash",
        IdempotencyKey = key,
        Tax = tax,
        CashTendered = cashTendered,
        Items = [new CreateSaleItemDto { VariantId = 100, Quantity = 2, UnitPrice = unitPrice }]
    };

    private sealed class Scenario : IDisposable
    {
        private readonly CreateSaleCommandHandler _handler;

        private Scenario(RetailPOSDbContext db, CreateSaleCommandHandler handler)
        {
            Db = db;
            _handler = handler;
        }

        public RetailPOSDbContext Db { get; }

        public async Task<RetailPOS.Application.Sales.ReadModels.SaleReadModel> CompleteAsync(CreateSaleDto dto) =>
            await _handler.Handle(
                SaleCreationCommandTestFactory.ToCommand(dto),
                CancellationToken.None);

        public void AssertNothingWritten()
        {
            Assert.Empty(NewSales());
            Assert.Equal(10, Db.Inventories.Single(item => item.VariantId == 100).Quantity);
        }

        /// <summary>Sales created by the handler (the seeded exchange fixtures use fixed numbers).</summary>
        public List<Sale> NewSales() => Db.Sales.Where(sale => sale.SaleNumber.StartsWith("INV-")).ToList();

        public static Scenario Create(decimal taxRate = 0m)
        {
            var db = new RetailPOSDbContext(new DbContextOptionsBuilder<RetailPOSDbContext>()
                .UseInMemoryDatabase($"SaleCheckoutAuthorityTests_{Guid.NewGuid()}")
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);

            var session = new CashSession
            {
                Id = 21, OutletId = OutletId, BusinessId = 1, TerminalId = 1,
                BusinessDate = DateTime.UtcNow.Date, Status = "open", OpenedByUserId = CashierId
            };
            db.Businesses.AddRange(new Business { Id = 1, Name = "One" }, new Business { Id = 2, Name = "Two" });
            db.Outlets.AddRange(
                new Outlet { Id = OutletId, BusinessId = 1, Name = "Main" },
                new Outlet { Id = 12, BusinessId = 2, Name = "Other business outlet" });
            db.Users.Add(new User
            {
                Id = CashierId, BusinessId = 1, Name = "Cashier", Email = "cashier@example.test",
                PasswordHash = "test", OutletId = OutletId, IsActive = true
            });
            db.PosTerminals.Add(new PosTerminal { Id = 1, OutletId = OutletId, IsActive = true, IsDefault = true, Name = "T1" });
            db.CashSessions.Add(session);
            db.Products.AddRange(
                new Product { Id = 200, BusinessId = 1, Name = "Widget", BasePrice = 100m, CostPrice = 40m },
                new Product { Id = 201, BusinessId = 1, Name = "Gadget", BasePrice = 50m, CostPrice = 20m },
                new Product { Id = 202, BusinessId = 1, Name = "Retired", BasePrice = 10m, CostPrice = 5m, Status = ProductStatus.Inactive });
            db.ProductVariants.AddRange(
                new ProductVariant { Id = 100, BusinessId = 1, ProductId = 200, Sku = "W1", Name = "Widget" },
                new ProductVariant { Id = 101, BusinessId = 1, ProductId = 201, Sku = "G1", Name = "Gadget" },
                new ProductVariant { Id = 102, BusinessId = 1, ProductId = 202, Sku = "R1", Name = "Retired" });
            foreach (var variantId in new long[] { 100, 101, 102 })
                db.Inventories.Add(new InventoryEntity
                {
                    VariantId = variantId, LocationId = OutletId, LocationType = "outlet", Quantity = 10
                });

            db.Sales.AddRange(
                new Sale { Id = 90, OutletId = 12, Status = "completed", SaleNumber = "OTHER-1" },
                new Sale { Id = 91, OutletId = OutletId, Status = "voided", SaleNumber = "VOID-1" },
                new Sale { Id = 92, OutletId = OutletId, Status = "completed", SaleNumber = "REPL-1", ReplacementSaleId = 55 },
                new Sale { Id = 93, OutletId = OutletId, Status = "completed", SaleNumber = "OK-1" });
            db.SaveChanges();

            var validator = new Mock<ISaleCashSessionValidator>();
            validator.Setup(x => x.ValidateActiveForSaleAsync(OutletId, 1, CashierId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(session);
            var allocator = new Mock<ISaleInvoiceNumberAllocator>();
            allocator.Setup(x => x.AllocateAsync(It.IsAny<long>(), It.IsAny<long>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => TestInvoices.Issue($"INV-{Guid.NewGuid():N}"[..20]));
            var cache = new Mock<IPosCacheService>();
            cache.Setup(x => x.InvalidateStockAsync(It.IsAny<long>(), It.IsAny<long>())).Returns(Task.CompletedTask);
            var events = new Mock<ISaleEventPublisher>();
            events.Setup(x => x.PublishSaleCompletedAsync(It.IsAny<SaleCompletedEvent>())).Returns(Task.CompletedTask);

            var handler = SaleCreationCommandTestFactory.Create(
                db,
                validator.Object,
                allocator.Object,
                Mock.Of<IAuditService>(),
                Mock.Of<IStockLedgerWriter>(),
                cache.Object,
                events.Object,
                checkoutSettings: SaleCreationCommandTestFactory.TaxPolicy(taxRate));
            return new Scenario(db, handler);
        }

        public void Dispose() => Db.Dispose();
    }
}
