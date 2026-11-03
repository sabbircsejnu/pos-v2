using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RetailPOS.Application.Abstractions;
using RetailPOS.Application.Products.Abstractions;
using RetailPOS.Application.Products.ReadModels;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Products;

public sealed class ProductCombinationGenerationStore(
    RetailPOSDbContext dbContext,
    IProductIdentifierResolver identifierResolver,
    ILogger<ProductCombinationGenerationStore> logger,
    IBusinessScope businessScope)
    : IProductCombinationGenerationStore
{
    public async Task<ProductCombinationGenerationData?> GetGenerationDataAsync(
        long productId,
        IReadOnlyList<long>? variationIds,
        CancellationToken cancellationToken)
    {
        var businessId = businessScope.RequireBusinessId();
        var product = await dbContext.Products
            .AsNoTracking()
            .Where(candidate => candidate.Id == productId && candidate.BusinessId == businessId)
            .Select(candidate => new
            {
                candidate.Id,
                candidate.ProductCode,
                candidate.CategoryId,
                candidate.BasePrice
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (product is null)
        {
            return null;
        }

        var assignedVariationIds = await dbContext.ProductVariations
            .AsNoTracking()
            .Where(assignment => assignment.ProductId == productId)
            .Select(assignment => assignment.VariationId)
            .ToListAsync(cancellationToken);
        var selectedOptionsQuery = dbContext.ProductVariationSelectedOptions
            .AsNoTracking()
            .Where(selected => selected.ProductId == productId);
        if (variationIds is { Count: > 0 })
        {
            selectedOptionsQuery = selectedOptionsQuery
                .Where(selected => variationIds.Contains(selected.VariationId));
        }

        var selectedOptions = await selectedOptionsQuery
            .Include(selected => selected.Option)
                .ThenInclude(option => option.Variation)
            .ToListAsync(cancellationToken);

        IReadOnlyList<IReadOnlyList<ProductCombinationOption>> optionGroups;
        if (selectedOptions.Count == 0)
        {
            logger.LogWarning(
                "Product {ProductId} has no selected options recorded; falling back to all active options.",
                productId);
            var assignedQuery = dbContext.ProductVariations
                .AsNoTracking()
                .Where(assignment => assignment.ProductId == productId);
            if (variationIds is { Count: > 0 })
            {
                assignedQuery = assignedQuery
                    .Where(assignment => variationIds.Contains(assignment.VariationId));
            }

            var assigned = await assignedQuery
                .Include(assignment => assignment.Variation)
                    .ThenInclude(variation => variation.Options.Where(option => option.IsActive))
                .ToListAsync(cancellationToken);
            optionGroups = assigned
                .Select(assignment => (IReadOnlyList<ProductCombinationOption>)assignment.Variation.Options
                    .OrderBy(option => option.DisplayOrder)
                    .Select(option => new ProductCombinationOption(
                        assignment.VariationId,
                        option.Id,
                        assignment.Variation.Name,
                        option.Name,
                        option.DisplayOrder,
                        option.PriceAdjustment))
                    .ToArray())
                .ToList();
        }
        else
        {
            var options = selectedOptions
                .Select(selected => new ProductCombinationOption(
                    selected.VariationId,
                    selected.OptionId,
                    selected.Option.Variation.Name,
                    selected.Option.Name,
                    selected.Option.DisplayOrder,
                    selected.Option.PriceAdjustment))
                .ToList();
            optionGroups = options
                .GroupBy(option => option.VariationId)
                .OrderBy(group => group.Key)
                .Select(group => (IReadOnlyList<ProductCombinationOption>)group
                    .OrderBy(option => option.DisplayOrder)
                    .ToArray())
                .ToArray();
        }

        return new ProductCombinationGenerationData(
            product.Id,
            product.ProductCode,
            product.CategoryId,
            product.BasePrice,
            assignedVariationIds,
            optionGroups);
    }

    public async Task<IReadOnlyList<ProductCombinationModel>> CreateMissingCombinationsAsync(
        ProductCombinationGenerationData generationData,
        IReadOnlyList<ProductCombinationDraft> combinations,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var result = new List<ProductCombinationModel>();
            var skippedCount = 0;

            foreach (var combination in combinations)
            {
                var existingVariants = await dbContext.ProductVariants
                    .Include(variant => variant.ProductVariantOptions)
                    .Where(variant =>
                        variant.ProductId == generationData.ProductId
                        && variant.ProductVariantOptions.Count == combination.OptionIds.Count)
                    .ToListAsync(cancellationToken);
                var alreadyExists = existingVariants.Any(variant =>
                    variant.ProductVariantOptions
                        .Select(option => option.OptionId)
                        .OrderBy(optionId => optionId)
                        .SequenceEqual(combination.OptionIds));
                if (alreadyExists)
                {
                    skippedCount++;
                    continue;
                }

                var sku = await identifierResolver.ResolveVariantSkuAsync(
                    requestedSku: null,
                    mainProductCode: generationData.ProductCode!,
                    sizeCode: combination.SizeCode,
                    attributeCode: combination.AttributeCode,
                    cancellationToken: cancellationToken);
                var barcode = await identifierResolver.ResolveVariantBarcodeAsync(
                    requestedBarcode: null,
                    categoryId: generationData.CategoryId,
                    cancellationToken: cancellationToken);
                var variant = new ProductVariant
                {
                    BusinessId = businessScope.RequireBusinessId(),
                    ProductId = generationData.ProductId,
                    Name = combination.Name,
                    Sku = sku,
                    Barcode = barcode,
                    PriceAdjustment = combination.PriceAdjustment,
                    CostAdjustment = 0,
                    Attributes = combination.AttributesJson,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                dbContext.ProductVariants.Add(variant);
                await dbContext.SaveChangesAsync(cancellationToken);
                dbContext.ProductVariantOptions.AddRange(combination.OptionIds.Select(optionId =>
                    new ProductVariantOption
                    {
                        VariantId = variant.Id,
                        OptionId = optionId
                    }));
                await dbContext.SaveChangesAsync(cancellationToken);

                result.Add(new ProductCombinationModel(
                    variant.Id,
                    variant.Sku,
                    variant.Barcode,
                    variant.PriceAdjustment,
                    variant.CostAdjustment,
                    combination.OptionIds,
                    combination.Options,
                    combination.Name,
                    generationData.BasePrice + combination.PriceAdjustment));
            }

            await transaction.CommitAsync(cancellationToken);
            logger.LogInformation(
                "Generated {CreatedCount} new combinations for product {ProductId}, skipped {SkippedCount} duplicates",
                result.Count,
                generationData.ProductId,
                skippedCount);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
