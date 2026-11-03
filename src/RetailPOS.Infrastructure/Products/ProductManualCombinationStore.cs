using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Abstractions;
using RetailPOS.Application.Products.Abstractions;
using RetailPOS.Application.Products.ReadModels;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Products;

public sealed class ProductManualCombinationStore(RetailPOSDbContext dbContext, IBusinessScope businessScope)
    : IProductManualCombinationStore
{
    public async Task<ManualProductCombinationData?> GetCreationDataAsync(
        long productId,
        IReadOnlyList<long> optionIds,
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

        var options = await dbContext.VariationOptions
            .AsNoTracking()
            .Where(option => optionIds.Contains(option.Id))
            .Include(option => option.Variation)
            .ToListAsync(cancellationToken);
        var sortedOptionIds = optionIds.OrderBy(id => id).ToArray();
        var existingVariants = await dbContext.ProductVariants
            .AsNoTracking()
            .Where(variant =>
                variant.ProductId == productId
                && variant.ProductVariantOptions.Count == sortedOptionIds.Length)
            .Include(variant => variant.ProductVariantOptions)
            .ToListAsync(cancellationToken);
        var combinationAlreadyExists = existingVariants.Any(variant =>
            variant.ProductVariantOptions
                .Select(option => option.OptionId)
                .OrderBy(id => id)
                .SequenceEqual(sortedOptionIds));

        return new ManualProductCombinationData(
            product.Id,
            product.ProductCode,
            product.CategoryId,
            product.BasePrice,
            options.Select(option => new ProductCombinationOption(
                option.VariationId,
                option.Id,
                option.Variation?.Name ?? string.Empty,
                option.Name,
                option.DisplayOrder,
                option.PriceAdjustment)).ToArray(),
            combinationAlreadyExists);
    }

    public async Task<ProductCombinationModel> CreateAsync(
        ManualProductCombinationData data,
        IReadOnlyList<long> optionIds,
        string sku,
        string barcode,
        decimal priceAdjustment,
        decimal costAdjustment,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var variant = new ProductVariant
            {
                BusinessId = businessScope.RequireBusinessId(),
                ProductId = data.ProductId,
                Name = string.Join(" - ", data.Options.Select(option => option.OptionName)),
                Sku = sku,
                Barcode = barcode,
                PriceAdjustment = priceAdjustment,
                CostAdjustment = costAdjustment,
                Attributes = "{}",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            dbContext.ProductVariants.Add(variant);
            await dbContext.SaveChangesAsync(cancellationToken);

            dbContext.ProductVariantOptions.AddRange(optionIds.Select(optionId =>
                new ProductVariantOption
                {
                    VariantId = variant.Id,
                    OptionId = optionId
                }));
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new ProductCombinationModel(
                variant.Id,
                variant.Sku,
                variant.Barcode,
                variant.PriceAdjustment,
                variant.CostAdjustment,
                optionIds,
                data.Options,
                variant.Name,
                data.BasePrice + priceAdjustment);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
