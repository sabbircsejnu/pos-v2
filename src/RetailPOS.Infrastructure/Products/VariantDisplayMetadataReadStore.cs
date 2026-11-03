using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Products.Abstractions;
using RetailPOS.Application.Products.ReadModels;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Products;

public sealed class VariantDisplayMetadataReadStore(RetailPOSDbContext dbContext)
    : IVariantDisplayMetadataReadStore
{
    public async Task<IReadOnlyList<VariantDisplayVariationReadModel>> GetAsync(
        CancellationToken cancellationToken)
    {
        var variations = await dbContext.Variations
            .AsNoTracking()
            .Where(variation => variation.IsActive)
            .OrderBy(variation => variation.DisplayOrder)
            .Select(variation => new
            {
                variation.Name,
                variation.DisplayOrder,
                ActiveOptionNames = variation.Options
                    .Where(option => option.IsActive)
                    .Select(option => option.Name)
            })
            .ToListAsync(cancellationToken);

        return variations
            .Select(variation => new VariantDisplayVariationReadModel(
                variation.Name,
                variation.DisplayOrder,
                variation.ActiveOptionNames
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .ToArray()))
            .ToArray();
    }
}
