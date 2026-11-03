using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Products.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Products;

public sealed class ProductVariationCommandStore(RetailPOSDbContext dbContext)
    : IProductVariationCommandStore
{
    public async Task AssignAsync(
        long productId,
        IReadOnlyList<long> variationIds,
        IReadOnlyDictionary<long, List<long>> selectedOptionsByVariation,
        CancellationToken cancellationToken)
    {
        var product = await dbContext.Products
            .Include(candidate => candidate.ProductVariations)
            .FirstOrDefaultAsync(candidate => candidate.Id == productId, cancellationToken);

        if (product is null)
        {
            throw new KeyNotFoundException($"Product with ID {productId} not found");
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var existingVariationIds = product.ProductVariations
                .Select(productVariation => productVariation.VariationId)
                .ToList();
            var removedAssignments = product.ProductVariations
                .Where(productVariation => !variationIds.Contains(productVariation.VariationId))
                .ToList();
            dbContext.RemoveRange(removedAssignments);

            var addedAssignments = variationIds
                .Where(variationId => !existingVariationIds.Contains(variationId))
                .Select(variationId => new ProductVariation
                {
                    ProductId = productId,
                    VariationId = variationId,
                    IsRequired = false,
                    CreatedAt = DateTime.UtcNow
                });
            await dbContext.AddRangeAsync(addedAssignments, cancellationToken);

            product.HasVariants = variationIds.Any();
            product.UpdatedAt = DateTime.UtcNow;

            var existingSelectedOptions = await dbContext.ProductVariationSelectedOptions
                .Where(selected => selected.ProductId == productId)
                .ToListAsync(cancellationToken);
            dbContext.RemoveRange(existingSelectedOptions);

            foreach (var variationId in variationIds)
            {
                List<long> optionIds;
                if (selectedOptionsByVariation.TryGetValue(variationId, out var providedOptions)
                    && providedOptions.Count > 0)
                {
                    optionIds = providedOptions;
                }
                else
                {
                    optionIds = await dbContext.VariationOptions
                        .Where(option => option.VariationId == variationId && option.IsActive)
                        .Select(option => option.Id)
                        .ToListAsync(cancellationToken);
                }

                var selectedOptions = optionIds.Select(optionId => new ProductVariationSelectedOption
                {
                    ProductId = productId,
                    VariationId = variationId,
                    OptionId = optionId,
                    CreatedAt = DateTime.UtcNow
                });
                await dbContext.AddRangeAsync(selectedOptions, cancellationToken);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}
