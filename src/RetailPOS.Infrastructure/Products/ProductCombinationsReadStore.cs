using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Products.Abstractions;
using RetailPOS.Application.Products.ReadModels;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Products;

public sealed class ProductCombinationsReadStore(RetailPOSDbContext dbContext)
    : IProductCombinationsReadStore
{
    public async Task<IReadOnlyList<ProductCombinationModel>?> GetByProductIdAsync(
        long productId,
        CancellationToken cancellationToken)
    {
        var basePrice = await dbContext.Products
            .AsNoTracking()
            .Where(product => product.Id == productId)
            .Select(product => (decimal?)product.BasePrice)
            .SingleOrDefaultAsync(cancellationToken);
        if (basePrice is null)
        {
            return null;
        }

        var variants = await dbContext.ProductVariants
            .AsNoTracking()
            .Where(variant => variant.ProductId == productId)
            .Include(variant => variant.ProductVariantOptions)
                .ThenInclude(option => option.Option)
                    .ThenInclude(option => option.Variation)
            .ToListAsync(cancellationToken);

        return variants.Select(variant => new ProductCombinationModel(
            variant.Id,
            variant.Sku,
            variant.Barcode,
            variant.PriceAdjustment,
            variant.CostAdjustment,
            variant.ProductVariantOptions.Select(option => option.OptionId).ToArray(),
            variant.ProductVariantOptions.Select(option => new ProductCombinationOption(
                option.Option.VariationId,
                option.Option.Id,
                option.Option.Variation?.Name ?? string.Empty,
                option.Option.Name,
                option.Option.DisplayOrder,
                option.Option.PriceAdjustment)).ToArray(),
            variant.Name,
            basePrice.Value + variant.PriceAdjustment))
            .ToArray();
    }
}
