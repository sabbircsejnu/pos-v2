using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Products.Abstractions;
using RetailPOS.Application.Products.ReadModels;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Products;

public sealed class ProductVariationsReadStore(RetailPOSDbContext dbContext)
    : IProductVariationsReadStore
{
    public async Task<ProductVariationsReadModel?> FindByProductIdAsync(
        long productId,
        CancellationToken cancellationToken)
    {
        var product = await dbContext.Products
            .AsNoTracking()
            .Include(candidate => candidate.ProductVariations)
                .ThenInclude(productVariation => productVariation.Variation)
                    .ThenInclude(variation => variation.Options.OrderBy(option => option.DisplayOrder))
            .FirstOrDefaultAsync(candidate => candidate.Id == productId, cancellationToken);

        if (product is null)
        {
            return null;
        }

        var selectedOptions = await dbContext.ProductVariationSelectedOptions
            .AsNoTracking()
            .Where(selected => selected.ProductId == productId)
            .Select(selected => new { selected.VariationId, selected.OptionId })
            .ToListAsync(cancellationToken);

        var selectedByVariation = selectedOptions
            .GroupBy(selected => selected.VariationId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(selected => selected.OptionId).ToArray());

        var variations = product.ProductVariations
            .Select(productVariation =>
            {
                var options = productVariation.Variation.Options
                    .Select(option => new VariationOptionReadModel(
                        option.Id,
                        option.Name,
                        option.PriceAdjustment))
                    .ToArray();
                var selectedOptionIds =
                    selectedByVariation.TryGetValue(productVariation.VariationId, out var selectedIds)
                    && selectedIds.Length > 0
                        ? selectedIds
                        : options.Select(option => option.Id).ToArray();

                return new ProductVariationReadModel(
                    productVariation.VariationId,
                    productVariation.Variation.Name,
                    productVariation.IsRequired,
                    options,
                    selectedOptionIds);
            })
            .ToArray();

        return new ProductVariationsReadModel(variations);
    }
}
