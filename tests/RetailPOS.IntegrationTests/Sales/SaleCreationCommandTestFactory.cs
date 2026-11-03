using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using RetailPOS.API.DTOs.Sale;
using RetailPOS.API.Services;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Pos.Abstractions;
using RetailPOS.Application.Pos.Queries;
using RetailPOS.Application.Pos.ReadModels;
using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Application.Sales.Checkout;
using RetailPOS.Application.Sales.Commands;
using RetailPOS.Application.Sales.FinancialPostings;
using RetailPOS.Application.Integration;
using RetailPOS.Core.Audit;
using RetailPOS.Infrastructure.Integration;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Sales.ReadModels;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Pos;
using RetailPOS.Infrastructure.Sales;
using Moq;

namespace RetailPOS.Tests.Sales;

internal static class SaleCreationCommandTestFactory
{
    /// <summary>Real server pricing pipeline over the test database, with no cache.</summary>
    public static IPosCartPriceCalculator PriceCalculator(
        RetailPOSDbContext db,
        ISaleCheckoutSettingsProvider? checkoutSettings = null,
        long businessId = 1)
    {
        var scope = new FixedBusinessScope(businessId);
        return new PosCartPriceCalculator(
            new PosCartPricingStore(db, scope),
            new NoCartPriceCache(),
            checkoutSettings ?? TaxPolicy(),
            scope,
            new NoPricingCacheVersion());
    }

    public static ISaleCheckoutSettingsProvider TaxPolicy(
        decimal ratePercent = 0m,
        bool pricesIncludeTax = false,
        decimal maxManualDiscountPercent = 100m) =>
        new FixedTaxPolicy(
            new SaleTaxPolicy(ratePercent > 0m, ratePercent, pricesIncludeTax),
            new SaleDiscountPolicy(maxManualDiscountPercent));

    internal sealed class FixedBusinessScope(long businessId) : RetailPOS.Application.Abstractions.IBusinessScope
    {
        public long? EffectiveBusinessId => businessId;
        public long RequireBusinessId() => businessId;
    }

    private sealed class NoPricingCacheVersion : IPosPricingCacheVersion
    {
        public Task<string?> GetTokenAsync(long businessId, long? outletId, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);

        public Task InvalidateAsync(long businessId, long? outletId, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class NoCartPriceCache : IPosCartPriceCache
    {
        public Task<PosCartPriceReadModel?> GetAsync(string scope, long variantId, long? outletId, int quantity, CancellationToken cancellationToken) =>
            Task.FromResult<PosCartPriceReadModel?>(null);

        public Task SetAsync(string scope, long variantId, long? outletId, int quantity, PosCartPriceReadModel result, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class FixedTaxPolicy(SaleTaxPolicy policy, SaleDiscountPolicy discountPolicy) : ISaleCheckoutSettingsProvider
    {
        public Task<SaleTaxPolicy> GetTaxPolicyAsync(long businessId, CancellationToken cancellationToken) => Task.FromResult(policy);

        public Task<SaleDiscountPolicy> GetDiscountPolicyAsync(long businessId, CancellationToken cancellationToken) =>
            Task.FromResult(discountPolicy);
    }

    public static CreateSaleCommandHandler Create(
        RetailPOSDbContext db,
        ISaleCashSessionValidator cashSessionValidator,
        ISaleInvoiceNumberAllocator invoiceNumberAllocator,
        IAuditService audit,
        IStockLedgerWriter stockLedger,
        IPosCacheService cache,
        ISaleEventPublisher events,
        ISaleCheckoutSettingsProvider? checkoutSettings = null,
        long businessId = 1)
    {
        return new(
            new SaleCreationStore(db, new SaleReadStore(db)),
            cashSessionValidator,
            invoiceNumberAllocator,
            FinancialPostings(db),
            new SaleCreationAuditWriter(audit),
            new SaleStockLedgerWriter(stockLedger),
            new SaleStockCacheInvalidator(cache),
            new SaleCompletedPublisher(events),
            new SaleCreationLogger(NullLogger<SaleCreationLogger>.Instance),
            PriceCalculator(db, checkoutSettings, businessId),
            checkoutSettings ?? TaxPolicy());
    }

    /// <summary>The production D10 intent writer over the test context (outbox included, no worker signal).</summary>
    public static ISaleFinancialPostingWriter FinancialPostings(RetailPOSDbContext db, TimeProvider? clock = null) =>
        new SaleFinancialPostingWriter(
            db,
            new CashSessionEventWriter(new IntegrationOutbox(
                db, Mock.Of<IAuditContext>(), TimeProvider.System, Mock.Of<IIntegrationReplayAuthorization>())),
            clock ?? TimeProvider.System);

    /// <summary>Posts the sale's D10 intent through the production journal poster (what the worker does).</summary>
    public static async Task<SaleJournalPostingResult> PostSaleIntentAsync(
        RetailPOSDbContext db,
        long saleId,
        ISender journalSender,
        IAccountingConfigurationService accountingConfiguration)
    {
        var posting = await db.SaleFinancialPostings.AsNoTracking()
            .SingleAsync(row => row.SaleId == saleId && row.Kind == RetailPOS.Core.Entities.SaleFinancialPostingKinds.Sale);
        return await new SaleFinancialJournalPoster(journalSender, accountingConfiguration)
            .PostSaleAsync(SaleFinancialIntentFactory.ReadSaleSnapshot(posting), CancellationToken.None);
    }

    /// <summary>Tests that are not about discount authorisation run as a caller holding both discount permissions.</summary>
    public static CreateSaleCommand ToCommand(
        CreateSaleDto dto,
        bool canApplyDiscount = true,
        bool canOverrideDiscountLimit = true) =>
        new(
            dto.OutletId,
            dto.TerminalId,
            dto.CustomerId,
            dto.Items.Select(item => new CreateSaleLine(
                item.VariantId,
                item.Quantity,
                item.UnitPrice,
                item.DiscountAmount,
                item.AppliedRuleId,
                item.AppliedRuleName)).ToArray(),
            dto.Discount,
            dto.Tax,
            dto.PaymentMethod,
            dto.Payments.Select(payment => new CreateSalePayment(
                payment.Method,
                payment.Amount,
                payment.Tendered)).ToArray(),
            dto.CashierId ?? 0,
            dto.OriginalSaleId,
            dto.SalesDate,
            dto.IdempotencyKey,
            dto.CashTendered,
            canApplyDiscount,
            canOverrideDiscountLimit);

    public static Task<SaleReadModel> ExecuteAsync(
        CreateSaleCommandHandler handler,
        CreateSaleDto dto,
        CancellationToken cancellationToken = default,
        bool canApplyDiscount = true,
        bool canOverrideDiscountLimit = true) =>
        handler.Handle(ToCommand(dto, canApplyDiscount, canOverrideDiscountLimit), cancellationToken);
}
