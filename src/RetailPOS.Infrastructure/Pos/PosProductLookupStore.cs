using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Abstractions;
using RetailPOS.Application.Pos.Abstractions;
using RetailPOS.Application.Pos.ReadModels;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Pos;

public sealed class PosProductLookupStore(RetailPOSDbContext dbContext, IBusinessScope businessScope) : IPosProductLookupStore
{
    public Task<PosProductLookupVariantReadModel?> FindVariantByBarcodeAsync(
        string barcode,
        CancellationToken cancellationToken)
    {
        var businessId = businessScope.RequireBusinessId();
        return dbContext.ProductVariants
            .AsNoTracking()
            .Where(variant => variant.BusinessId == businessId && variant.Barcode == barcode)
            .Select(variant => new PosProductLookupVariantReadModel(
                variant.Id,
                variant.ProductId,
                variant.Product.Name,
                variant.Name,
                variant.Barcode,
                variant.Sku ?? string.Empty,
                variant.Product.Images
                    .Where(image => image.IsPrimary)
                    .Select(image => image.ThumbPath)
                    .FirstOrDefault(),
                variant.Product.BasePrice + variant.PriceAdjustment,
                variant.Product.TaxRate,
                variant.Product.Status == ProductStatus.Active,
                variant.Product.TaxClassification))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<PosProductLookupVariantReadModel?> FindFirstActiveProductVariantByBarcodeAsync(
        string barcode,
        CancellationToken cancellationToken)
    {
        var businessId = businessScope.RequireBusinessId();
        return dbContext.Products
            .AsNoTracking()
            .Where(product =>
                product.BusinessId == businessId &&
                product.Barcode == barcode &&
                product.Status == ProductStatus.Active)
            .SelectMany(
                product => product.ProductVariants.Take(1),
                (product, variant) => new PosProductLookupVariantReadModel(
                    variant.Id,
                    product.Id,
                    product.Name,
                    variant.Name,
                    product.Barcode,
                    variant.Sku ?? string.Empty,
                    product.Images
                        .Where(image => image.IsPrimary)
                        .Select(image => image.ThumbPath)
                        .FirstOrDefault(),
                    product.BasePrice + variant.PriceAdjustment,
                    product.TaxRate,
                    product.Status == ProductStatus.Active,
                    product.TaxClassification))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<PosProductLookupVariantReadModel?> FindVariantBySkuAsync(
        string sku,
        CancellationToken cancellationToken)
    {
        var businessId = businessScope.RequireBusinessId();
        return dbContext.ProductVariants
            .AsNoTracking()
            .Where(variant => variant.BusinessId == businessId && variant.Sku == sku)
            .Select(variant => new PosProductLookupVariantReadModel(
                variant.Id,
                variant.ProductId,
                variant.Product.Name,
                variant.Name,
                variant.Barcode,
                variant.Sku ?? string.Empty,
                variant.Product.Images
                    .Where(image => image.IsPrimary)
                    .Select(image => image.ThumbPath)
                    .FirstOrDefault(),
                variant.Product.BasePrice + variant.PriceAdjustment,
                variant.Product.TaxRate,
                variant.Product.Status == ProductStatus.Active,
                variant.Product.TaxClassification))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<PosProductLookupVariantReadModel?> FindVariantByIdAsync(
        long variantId,
        CancellationToken cancellationToken)
    {
        var businessId = businessScope.RequireBusinessId();
        return dbContext.ProductVariants
            .AsNoTracking()
            .Where(variant => variant.BusinessId == businessId && variant.Id == variantId)
            .Select(variant => new PosProductLookupVariantReadModel(
                variant.Id,
                variant.ProductId,
                variant.Product.Name,
                variant.Name,
                variant.Barcode,
                variant.Sku ?? string.Empty,
                variant.Product.Images
                    .Where(image => image.IsPrimary)
                    .Select(image => image.ThumbPath)
                    .FirstOrDefault(),
                variant.Product.BasePrice + variant.PriceAdjustment,
                variant.Product.TaxRate,
                variant.Product.Status == ProductStatus.Active,
                variant.Product.TaxClassification))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<decimal> GetOutletStockAsync(
        long variantId,
        long outletId,
        CancellationToken cancellationToken) =>
        dbContext.Inventories
            .Where(inventory =>
                inventory.VariantId == variantId &&
                inventory.LocationId == outletId &&
                inventory.LocationType == "outlet")
            .Select(inventory => inventory.Quantity)
            .FirstOrDefaultAsync(cancellationToken);

}
