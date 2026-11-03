using MediatR;
using RetailPOS.Application.Abstractions;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Pos.Abstractions;
using RetailPOS.Application.Pos.ReadModels;
using RetailPOS.Application.Sales.Abstractions;

namespace RetailPOS.Application.Pos.Queries;

public sealed record GetPosProductLookupQuery(
    string Query,
    PosProductLookupMode Mode,
    long? OutletId) : IQuery<PosProductLookupReadModel?>;

public sealed class GetPosProductLookupQueryHandler(
    IPosProductLookupStore store,
    IPosProductLookupCache cache,
    IPosCartPriceCalculator priceCalculator,
    ISaleCheckoutSettingsProvider checkoutSettings,
    IBusinessScope businessScope,
    IPosPricingCacheVersion cacheVersion)
    : IRequestHandler<GetPosProductLookupQuery, PosProductLookupReadModel?>
{
    public async Task<PosProductLookupReadModel?> Handle(
        GetPosProductLookupQuery request,
        CancellationToken cancellationToken)
    {
        if (request.Mode == PosProductLookupMode.Variant &&
            !long.TryParse(request.Query, out _))
        {
            return null;
        }

        // The lookup cache is shared across Businesses, so every key carries the Business, tax policy and pricing version.
        var businessId = businessScope.RequireBusinessId();
        var taxPolicy = await checkoutSettings.GetTaxPolicyAsync(businessId, cancellationToken);
        var versionToken = await cacheVersion.GetTokenAsync(businessId, request.OutletId, cancellationToken);
        var cacheQuery = versionToken is null
            ? null
            : $"{businessId}:{(taxPolicy.Enabled ? 1 : 0)}:{taxPolicy.RatePercent}:{(taxPolicy.PricesIncludeTax ? 1 : 0)}:{versionToken}:{request.Query}";

        var cached = cacheQuery is null
            ? null
            : await cache.GetLookupAsync(
                request.Mode,
                cacheQuery,
                request.OutletId,
                cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var variant = request.Mode switch
        {
            PosProductLookupMode.Sku =>
                await store.FindVariantBySkuAsync(request.Query, cancellationToken),
            PosProductLookupMode.Variant when long.TryParse(request.Query, out var variantId) =>
                await store.FindVariantByIdAsync(variantId, cancellationToken),
            PosProductLookupMode.Variant => null,
            _ => await FindByBarcodeAsync(request.Query, cancellationToken)
        };

        if (variant is null || !variant.IsActive)
        {
            return null;
        }

        var prices = await priceCalculator.CalculateAsync(
            [new PosCartPriceLine(variant.VariantId, request.OutletId, 1)],
            true,
            cancellationToken);
        var price = prices.Single();

        var stockQuantity = 0;
        var stockFromCache = false;
        if (request.OutletId.HasValue)
        {
            var cachedStock = await cache.GetStockAsync(
                variant.VariantId,
                request.OutletId.Value,
                cancellationToken);
            if (cachedStock.HasValue)
            {
                stockQuantity = cachedStock.Value;
                stockFromCache = true;
            }
            else
            {
                stockQuantity = await store.GetOutletStockAsync(
                    variant.VariantId,
                    request.OutletId.Value,
                    cancellationToken);
                await cache.SetStockAsync(
                    variant.VariantId,
                    request.OutletId.Value,
                    stockQuantity,
                    cancellationToken);
            }
        }

        var result = new PosProductLookupReadModel(
            variant.VariantId,
            variant.ProductId,
            variant.ProductName,
            variant.VariantName,
            variant.Barcode,
            variant.Sku,
            variant.ImageUrl,
            variant.BasePrice,
            price.FinalPrice,
            price.TaxRate,
            price.AppliedRuleName,
            stockQuantity,
            stockQuantity > 0,
            stockFromCache,
            price.TaxClassification,
            price.PricesIncludeTax);

        if (cacheQuery is not null)
        {
            await cache.SetLookupAsync(
                request.Mode,
                cacheQuery,
                request.OutletId,
                result,
                cancellationToken);
        }

        return result;
    }

    private async Task<PosProductLookupVariantReadModel?> FindByBarcodeAsync(
        string barcode,
        CancellationToken cancellationToken)
    {
        var variant = await store.FindVariantByBarcodeAsync(barcode, cancellationToken);
        return variant ?? await store.FindFirstActiveProductVariantByBarcodeAsync(barcode, cancellationToken);
    }
}
