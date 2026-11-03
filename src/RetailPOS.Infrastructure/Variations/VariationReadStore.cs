using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Variations;
using RetailPOS.Application.Variations.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Variations;

public sealed class VariationReadStore(RetailPOSDbContext dbContext)
    : IVariationReadStore
{
    public async Task<IReadOnlyList<VariationReadModel>> GetAllAsync(
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Variations
            .AsNoTracking()
            .Include(variation => variation.Options)
            .AsQueryable();
        if (!includeInactive)
            query = query.Where(variation => variation.IsActive);

        var variations = await query
            .OrderBy(variation => variation.DisplayOrder)
            .ToListAsync(cancellationToken);
        return variations.Select(ToReadModel).ToList();
    }

    public async Task<VariationReadModel?> GetByIdAsync(
        long variationId,
        CancellationToken cancellationToken)
    {
        var variation = await dbContext.Variations
            .AsNoTracking()
            .Include(candidate => candidate.Options)
            .FirstOrDefaultAsync(candidate => candidate.Id == variationId, cancellationToken);
        return variation is null ? null : ToReadModel(variation);
    }

    public Task<VariationOptionReadModel?> GetOptionByIdAsync(
        long optionId,
        CancellationToken cancellationToken) =>
        dbContext.VariationOptions
            .AsNoTracking()
            .Where(option => option.Id == optionId)
            .Select(option => new VariationOptionReadModel(
                option.Id,
                option.VariationId,
                option.Variation.Name,
                option.Name,
                option.PriceAdjustment,
                option.DisplayOrder,
                option.IsActive))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<VariationProductReference>> GetProductsUsingVariationAsync(
        long variationId,
        CancellationToken cancellationToken) =>
        await dbContext.ProductVariations
            .AsNoTracking()
            .Where(productVariation => productVariation.VariationId == variationId)
            .Select(productVariation => productVariation.Product)
            .Distinct()
            .OrderBy(product => product.Name)
            .Select(product => new VariationProductReference(product.Id, product.Name))
            .ToListAsync(cancellationToken);

    internal static VariationReadModel ToReadModel(Variation variation) =>
        new(
            variation.Id,
            variation.Name,
            variation.DisplayOrder,
            variation.IsActive,
            variation.AutoSelectAllOptions,
            variation.Options
                .OrderBy(option => option.DisplayOrder)
                .Select(option => new VariationOptionReadModel(
                    option.Id,
                    option.VariationId,
                    option.Variation?.Name ?? variation.Name,
                    option.Name,
                    option.PriceAdjustment,
                    option.DisplayOrder,
                    option.IsActive))
                .ToList());
}
