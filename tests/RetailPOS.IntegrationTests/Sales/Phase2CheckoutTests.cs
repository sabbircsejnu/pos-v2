using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using RetailPOS.API.DTOs.Sale;
using RetailPOS.API.Services;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Pos.Queries;
using RetailPOS.Application.Pos.ReadModels;
using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Application.Sales.Checkout;
using RetailPOS.Application.Sales.Commands;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.Data;
using InventoryEntity = RetailPOS.Core.Entities.Inventory;

namespace RetailPOS.Tests.Sales;

/// <summary>
/// Phase 2: product-tax policy (P2.1), discount authorisation (P2.2), tenant isolation at checkout, terminal
/// resolution and the structured invoice fields, exercised through the real checkout handler.
/// </summary>
public sealed class Phase2CheckoutTests
{
    private const long OutletId = 11;
    private const long CashierId = 5;

    // Variants: 100 no product rate, 101 product rate 5%, 102 explicit 0%, 103 exempt (rate ignored), 104 belongs to business 2.
    private static CreateSaleDto Request(string key, params (long VariantId, int Quantity, decimal UnitPrice)[] lines) => new()
    {
        OutletId = OutletId,
        TerminalId = 1,
        CashierId = CashierId,
        PaymentMethod = "cash",
        IdempotencyKey = key,
        CashTendered = 10_000m,
        Items = lines.Select(line => new CreateSaleItemDto
        {
            VariantId = line.VariantId, Quantity = line.Quantity, UnitPrice = line.UnitPrice
        }).ToList()
    };

    [Fact]
    public async Task ProductRate_OverridesTheBusinessDefault()
    {
        using var scenario = Scenario.Create(taxRate: 10m);
        var request = Request("p2-override", (101, 1, 100m));
        request.Tax = 5m;

        var sale = await scenario.CompleteAsync(request);

        var item = Assert.Single(scenario.SavedItems(sale.Id));
        Assert.Equal(5m, item.VatRate);
        Assert.Equal("standard", item.TaxClassification);
        Assert.Equal(5m, sale.Tax);
        Assert.Equal(105m, sale.TotalAmount);
    }

    [Fact]
    public async Task MissingProductRate_UsesTheBusinessDefault()
    {
        using var scenario = Scenario.Create(taxRate: 10m);
        var request = Request("p2-fallback", (100, 1, 100m));
        request.Tax = 10m;

        var sale = await scenario.CompleteAsync(request);

        Assert.Equal(10m, Assert.Single(scenario.SavedItems(sale.Id)).VatRate);
        Assert.Equal(110m, sale.TotalAmount);
    }

    [Fact]
    public async Task ExplicitZeroRate_IsZeroRatedAndDoesNotFallBackToTheDefault()
    {
        using var scenario = Scenario.Create(taxRate: 10m);

        var sale = await scenario.CompleteAsync(Request("p2-zero", (102, 1, 100m)));

        var item = Assert.Single(scenario.SavedItems(sale.Id));
        Assert.Equal(0m, item.VatRate);
        Assert.Equal("zero_rated", item.TaxClassification);
        Assert.Equal(0m, sale.Tax);
        Assert.Equal(100m, sale.TotalAmount);
    }

    [Fact]
    public async Task ExemptClassification_ChargesNoTaxEvenWithAProductRate()
    {
        using var scenario = Scenario.Create(taxRate: 10m);

        var sale = await scenario.CompleteAsync(Request("p2-exempt", (103, 1, 100m)));

        var item = Assert.Single(scenario.SavedItems(sale.Id));
        Assert.Equal("exempt", item.TaxClassification);
        Assert.Equal(0m, sale.Tax);
    }

    [Fact]
    public async Task MixedRatesWithAnInvoiceDiscount_TaxEachRateAfterTheDiscountAndSnapshotEachLine()
    {
        using var scenario = Scenario.Create(taxRate: 10m);
        var request = Request("p2-mixed", (100, 1, 100m), (102, 1, 100m));
        request.Discount = 20m;
        request.Tax = 9m;

        var sale = await scenario.CompleteAsync(request);

        // Discount shares 10/10 -> bases 90/90; only the 10% line carries VAT: 9.00.
        Assert.Equal(9m, sale.Tax);
        Assert.Equal(189m, sale.TotalAmount);
        var items = scenario.SavedItems(sale.Id);
        Assert.Equal(sale.Tax, items.Sum(item => item.VatAmount));
        Assert.Equal(180m, items.Sum(item => item.TaxableAmount));
        Assert.Equal(new[] { 10m, 0m }, items.OrderBy(item => item.VariantId).Select(item => item.VatRate!.Value));
    }

    [Fact]
    public async Task InclusivePrices_ExtractTaxFromThePayableWithoutChangingIt()
    {
        using var scenario = Scenario.Create(taxRate: 10m, pricesIncludeTax: true);
        var request = Request("p2-inclusive", (100, 1, 100m), (101, 1, 100m));
        request.Tax = 13.85m;

        var sale = await scenario.CompleteAsync(request);

        // 100 @10% inclusive -> 9.09 (100*10/110); 100 @5% inclusive -> 4.76 (100*5/105), rounded per rate group.
        Assert.Equal(13.85m, sale.Tax);
        Assert.Equal(200m, sale.TotalAmount);
        var items = scenario.SavedItems(sale.Id).OrderBy(item => item.VariantId).ToList();
        Assert.Equal(new[] { 9.09m, 4.76m }, items.Select(item => item.VatAmount!.Value));
        Assert.Equal(new[] { 90.91m, 95.24m }, items.Select(item => item.TaxableAmount!.Value));
        Assert.All(items, item => Assert.True(item.TaxInclusive));
    }

    [Fact]
    public async Task CartPrices_AndCheckoutAgreeOnTheEffectiveRateForEveryProduct()
    {
        using var scenario = Scenario.Create(taxRate: 10m);
        var calculator = SaleCreationCommandTestFactory.PriceCalculator(
            scenario.Db, SaleCreationCommandTestFactory.TaxPolicy(10m));

        var prices = await calculator.CalculateAsync(
            [new PosCartPriceLine(100, OutletId, 1), new PosCartPriceLine(101, OutletId, 1),
             new PosCartPriceLine(102, OutletId, 1), new PosCartPriceLine(103, OutletId, 1)],
            readFromCache: false,
            CancellationToken.None);

        Assert.Equal(new[] { 10m, 5m, 0m, 0m }, prices.Select(price => price.TaxRate));
        Assert.Equal(new[] { "standard", "standard", "zero_rated", "exempt" }, prices.Select(price => price.TaxClassification));

        var request = Request("p2-agree", (100, 1, 100m), (101, 1, 100m), (102, 1, 100m), (103, 1, 100m));
        request.Tax = 15m;
        var sale = await scenario.CompleteAsync(request);
        var saved = scenario.SavedItems(sale.Id).OrderBy(item => item.VariantId).ToList();
        Assert.Equal(prices.Select(price => price.TaxRate), saved.Select(item => item.VatRate!.Value));
        Assert.Equal(prices.Select(price => price.TaxClassification), saved.Select(item => item.TaxClassification));
    }

    [Fact]
    public async Task ManualDiscount_WithoutThePermissionIsRefusedAndNothingIsWritten()
    {
        using var scenario = Scenario.Create(maxManualDiscountPercent: 50m);
        var request = Request("p2-discount-denied", (100, 1, 100m));
        request.Items[0].DiscountAmount = 5m;

        var exception = await Assert.ThrowsAsync<SaleCheckoutForbiddenException>(() =>
            scenario.CompleteAsync(request, canApplyDiscount: false));

        Assert.Equal(SaleCheckoutErrorCodes.DiscountNotPermitted, exception.Code);
        Assert.Empty(scenario.NewSales());
    }

    [Fact]
    public async Task ManualDiscount_OverTheBusinessLimitIsRefusedNotSilentlyReduced()
    {
        using var scenario = Scenario.Create(maxManualDiscountPercent: 10m);
        var request = Request("p2-discount-over", (100, 1, 100m));
        request.Items[0].DiscountAmount = 10.01m;

        var exception = await Assert.ThrowsAsync<SaleCheckoutConflictException>(() =>
            scenario.CompleteAsync(request, canOverrideDiscountLimit: false));

        Assert.Equal(SaleCheckoutErrorCodes.DiscountLimitExceeded, exception.Code);
        Assert.Empty(scenario.NewSales());
    }

    [Fact]
    public async Task ManualDiscount_WithinTheLimitSucceedsAndIsAudited()
    {
        using var scenario = Scenario.Create(maxManualDiscountPercent: 10m);
        var request = Request("p2-discount-ok", (100, 1, 100m));
        request.Items[0].DiscountAmount = 10m;

        var sale = await scenario.CompleteAsync(request, canOverrideDiscountLimit: false);

        Assert.Equal(90m, sale.TotalAmount);
        var audit = Assert.Single(scenario.DiscountAudits());
        Assert.Equal(CashierId, audit.Metadata!["cashierId"]);
        Assert.Equal(10m, audit.Metadata["effectiveDiscountPercent"]);
        Assert.Equal(10m, audit.Metadata["limitPercent"]);
        Assert.Equal(false, audit.Metadata["overrideUsed"]);
    }

    [Fact]
    public async Task ManualDiscount_OverTheLimitWithTheOverridePermissionSucceedsAndRecordsTheOverride()
    {
        using var scenario = Scenario.Create(maxManualDiscountPercent: 10m);
        var request = Request("p2-discount-override", (100, 1, 100m));
        request.Items[0].DiscountAmount = 40m;

        var sale = await scenario.CompleteAsync(request, canOverrideDiscountLimit: true);

        Assert.Equal(60m, sale.TotalAmount);
        var audit = Assert.Single(scenario.DiscountAudits());
        Assert.Equal(true, audit.Metadata!["overrideUsed"]);
        Assert.Equal(40m, audit.Metadata["effectiveDiscountPercent"]);
    }

    [Fact]
    public async Task AutomaticRuleDiscounts_DoNotCountAgainstTheManualLimit()
    {
        using var scenario = Scenario.Create(maxManualDiscountPercent: 0m);
        scenario.Db.PriceRules.Add(new PriceRule
        {
            BusinessId = 1, Name = "Half price", RuleType = "global", DiscountType = "percentage", DiscountValue = 50m, IsActive = true
        });
        await scenario.Db.SaveChangesAsync();

        // No manual discount is enabled (limit 0), but the server-side campaign is not a manual discount.
        var sale = await scenario.CompleteAsync(Request("p2-rule-discount", (100, 1, 50m)), canApplyDiscount: false);

        Assert.Equal(50m, sale.TotalAmount);
        Assert.Empty(scenario.DiscountAudits());
    }

    [Fact]
    public async Task Customer_OfAnotherBusinessIsReportedAsNotFound()
    {
        using var scenario = Scenario.Create();
        var request = Request("p2-foreign-customer", (100, 1, 100m));
        request.CustomerId = 902;

        await Assert.ThrowsAsync<KeyNotFoundException>(() => scenario.CompleteAsync(request));

        Assert.Empty(scenario.NewSales());
    }

    [Fact]
    public async Task Variant_OfAnotherBusinessCannotBeSoldOrPriced()
    {
        using var scenario = Scenario.Create();

        await Assert.ThrowsAnyAsync<Exception>(() => scenario.CompleteAsync(Request("p2-foreign-variant", (104, 1, 100m))));

        Assert.Empty(scenario.NewSales());
    }

    [Fact]
    public async Task WalkInCustomer_IsCreatedPerBusiness()
    {
        using var scenario = Scenario.Create();

        var sale = await scenario.CompleteAsync(Request("p2-walkin", (100, 1, 100m)));

        var walkIn = await scenario.Db.Customers.SingleAsync(customer => customer.Id == scenario.SavedSale(sale.Id).CustomerId);
        Assert.Equal(Customer.WalkInCustomerCode, walkIn.CustomerCode);
        Assert.Equal(1, walkIn.BusinessId);
        Assert.True(walkIn.IsSystem);
        Assert.Equal(1, await scenario.Db.Customers.CountAsync(customer => customer.CustomerCode == Customer.WalkInCustomerCode));
    }

    [Fact]
    public async Task Sale_PersistsTheBusinessAndTheStructuredInvoiceFields()
    {
        using var scenario = Scenario.Create();

        var sale = await scenario.CompleteAsync(Request("p2-invoice", (100, 1, 100m)));

        var saved = scenario.SavedSale(sale.Id);
        Assert.Equal(1, saved.BusinessId);
        Assert.Equal("T", saved.InvoicePrefix);
        Assert.NotNull(saved.InvoiceSequence);
        Assert.NotNull(saved.InvoiceIssuedAtUtc);
        Assert.Equal(DateTime.UtcNow.Date, saved.BusinessDate);
    }

    [Fact]
    public async Task Terminal_IsAutoSelectedOnlyWhenExactlyOneIsActive()
    {
        using var scenario = Scenario.Create();
        var request = Request("p2-one-terminal", (100, 1, 100m));
        request.TerminalId = null;

        var sale = await scenario.CompleteAsync(request);

        Assert.Equal(1, scenario.SavedSale(sale.Id).TerminalId);
    }

    [Fact]
    public async Task Terminal_MustBeChosenExplicitlyWhenSeveralAreActive()
    {
        using var scenario = Scenario.Create();
        scenario.Db.PosTerminals.Add(new PosTerminal { Id = 2, OutletId = OutletId, IsActive = true, IsDefault = true, Name = "T2", Code = "T02" });
        await scenario.Db.SaveChangesAsync();
        var request = Request("p2-two-terminals", (100, 1, 100m));
        request.TerminalId = null;

        var exception = await Assert.ThrowsAsync<SaleCheckoutValidationException>(() => scenario.CompleteAsync(request));

        Assert.Equal(SaleCheckoutErrorCodes.TerminalRequired, exception.Code);
    }

    [Fact]
    public async Task Terminal_NoActiveTerminalBlocksCheckout()
    {
        using var scenario = Scenario.Create();
        var terminal = await scenario.Db.PosTerminals.SingleAsync();
        terminal.IsActive = false;
        await scenario.Db.SaveChangesAsync();
        var request = Request("p2-no-terminal", (100, 1, 100m));
        request.TerminalId = null;

        var exception = await Assert.ThrowsAsync<SaleCheckoutValidationException>(() => scenario.CompleteAsync(request));

        Assert.Equal(SaleCheckoutErrorCodes.NoActiveTerminal, exception.Code);
    }

    private sealed class Scenario : IDisposable
    {
        private readonly CreateSaleCommandHandler _handler;
        private readonly List<AuditEventInput> _audits;

        private Scenario(RetailPOSDbContext db, CreateSaleCommandHandler handler, List<AuditEventInput> audits)
        {
            Db = db;
            _handler = handler;
            _audits = audits;
        }

        public RetailPOSDbContext Db { get; }

        public async Task<RetailPOS.Application.Sales.ReadModels.SaleReadModel> CompleteAsync(
            CreateSaleDto dto,
            bool canApplyDiscount = true,
            bool canOverrideDiscountLimit = true) =>
            await _handler.Handle(
                SaleCreationCommandTestFactory.ToCommand(dto, canApplyDiscount, canOverrideDiscountLimit),
                CancellationToken.None);

        public List<SaleItem> SavedItems(long saleId) => Db.SaleItems.Where(item => item.SaleId == saleId).ToList();

        public Sale SavedSale(long saleId) => Db.Sales.Single(sale => sale.Id == saleId);

        public List<Sale> NewSales() => Db.Sales.Where(sale => sale.SaleNumber.StartsWith("INV-")).ToList();

        public IEnumerable<AuditEventInput> DiscountAudits() =>
            _audits.Where(audit => audit.Summary.Contains("Manual discount", StringComparison.Ordinal));

        public static Scenario Create(
            decimal taxRate = 0m,
            bool pricesIncludeTax = false,
            decimal maxManualDiscountPercent = 100m)
        {
            var db = new RetailPOSDbContext(new DbContextOptionsBuilder<RetailPOSDbContext>()
                .UseInMemoryDatabase($"Phase2CheckoutTests_{Guid.NewGuid()}")
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);

            var session = new CashSession
            {
                Id = 21, OutletId = OutletId, BusinessId = 1, TerminalId = 1,
                BusinessDate = DateTime.UtcNow.Date, Status = "open", OpenedByUserId = CashierId
            };
            db.Businesses.AddRange(new Business { Id = 1, Name = "One" }, new Business { Id = 2, Name = "Two" });
            db.Outlets.AddRange(
                new Outlet { Id = OutletId, BusinessId = 1, Name = "Main", InvoicePrefix = "T" },
                new Outlet { Id = 12, BusinessId = 2, Name = "Other business outlet", InvoicePrefix = "U" });
            db.Users.Add(new User
            {
                Id = CashierId, BusinessId = 1, Name = "Cashier", Email = "cashier@example.test",
                PasswordHash = "test", OutletId = OutletId, IsActive = true
            });
            db.PosTerminals.Add(new PosTerminal { Id = 1, OutletId = OutletId, IsActive = true, Name = "T1", Code = "T01" });
            db.CashSessions.Add(session);
            db.Categories.AddRange(
                new Category { Id = 1, BusinessId = 1, Name = "Goods" },
                new Category { Id = 2, BusinessId = 2, Name = "Other goods" });
            db.Products.AddRange(
                new Product { Id = 200, BusinessId = 1, CategoryId = 1, Name = "Default tax", BasePrice = 100m, CostPrice = 40m },
                new Product { Id = 201, BusinessId = 1, CategoryId = 1, Name = "Five percent", BasePrice = 100m, CostPrice = 40m, TaxRate = 5m },
                new Product { Id = 202, BusinessId = 1, CategoryId = 1, Name = "Zero rated", BasePrice = 100m, CostPrice = 40m, TaxRate = 0m },
                new Product { Id = 203, BusinessId = 1, CategoryId = 1, Name = "Exempt", BasePrice = 100m, CostPrice = 40m, TaxRate = 15m, TaxClassification = "exempt" },
                new Product { Id = 204, BusinessId = 2, CategoryId = 2, Name = "Foreign", BasePrice = 100m, CostPrice = 40m });
            db.ProductVariants.AddRange(
                new ProductVariant { Id = 100, BusinessId = 1, ProductId = 200, Sku = "D1", Name = "Default tax" },
                new ProductVariant { Id = 101, BusinessId = 1, ProductId = 201, Sku = "F1", Name = "Five percent" },
                new ProductVariant { Id = 102, BusinessId = 1, ProductId = 202, Sku = "Z1", Name = "Zero rated" },
                new ProductVariant { Id = 103, BusinessId = 1, ProductId = 203, Sku = "E1", Name = "Exempt" },
                new ProductVariant { Id = 104, BusinessId = 2, ProductId = 204, Sku = "X1", Name = "Foreign" });
            foreach (var variantId in new long[] { 100, 101, 102, 103, 104 })
                db.Inventories.Add(new InventoryEntity
                {
                    VariantId = variantId, LocationId = variantId == 104 ? 12 : OutletId, LocationType = "outlet", Quantity = 50
                });
            db.Customers.Add(new Customer { Id = 902, BusinessId = 2, Name = "Foreign customer", IsActive = true });
            db.SaveChanges();

            var validator = new Mock<ISaleCashSessionValidator>();
            validator.Setup(x => x.ValidateActiveForSaleAsync(OutletId, It.IsAny<long>(), CashierId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(session);
            var allocator = new Mock<ISaleInvoiceNumberAllocator>();
            allocator.Setup(x => x.AllocateAsync(It.IsAny<long>(), It.IsAny<long>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => TestInvoices.Issue($"INV-{Guid.NewGuid():N}"[..20]));
            var cache = new Mock<IPosCacheService>();
            cache.Setup(x => x.InvalidateStockAsync(It.IsAny<long>(), It.IsAny<long>())).Returns(Task.CompletedTask);
            var events = new Mock<ISaleEventPublisher>();
            events.Setup(x => x.PublishSaleCompletedAsync(It.IsAny<SaleCompletedEvent>())).Returns(Task.CompletedTask);
            var audits = new List<AuditEventInput>();
            var audit = new Mock<IAuditService>();
            audit.Setup(x => x.RecordAsync(It.IsAny<AuditEventInput>(), It.IsAny<CancellationToken>()))
                .Callback<AuditEventInput, CancellationToken>((input, _) => audits.Add(input))
                .Returns(Task.CompletedTask);

            var handler = SaleCreationCommandTestFactory.Create(
                db,
                validator.Object,
                allocator.Object,
                audit.Object,
                Mock.Of<IStockLedgerWriter>(),
                cache.Object,
                events.Object,
                checkoutSettings: SaleCreationCommandTestFactory.TaxPolicy(taxRate, pricesIncludeTax, maxManualDiscountPercent));
            return new Scenario(db, handler, audits);
        }

        public void Dispose() => Db.Dispose();
    }
}
