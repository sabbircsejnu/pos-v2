using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Abstractions;
using RetailPOS.Application.Products.Abstractions;
using RetailPOS.Application.Products.ReadModels;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Products;

public sealed class ProductVariantMaintenanceStore(RetailPOSDbContext dbContext, IBusinessScope businessScope)
    : IProductVariantMaintenanceStore
{
    public async Task<ProductVariantUpdateData?> GetUpdateDataAsync(
        long variantId,
        CancellationToken cancellationToken)
    {
        var businessId = businessScope.RequireBusinessId();
        var variant = await dbContext.ProductVariants
            .AsNoTracking()
            .Include(candidate => candidate.Product)
            .Include(candidate => candidate.ProductVariantOptions)
                .ThenInclude(option => option.Option)
                    .ThenInclude(option => option.Variation)
            .FirstOrDefaultAsync(candidate => candidate.Id == variantId && candidate.BusinessId == businessId, cancellationToken);

        return variant is null
            ? null
            : new ProductVariantUpdateData(
                variant.Product.ProductCode,
                variant.Product.CategoryId,
                variant.ProductVariantOptions
                    .Select(option => (
                        option.Option.Variation?.Name ?? string.Empty,
                        option.Option.Name))
                    .ToArray());
    }

    public async Task SaveUpdateAsync(
        long variantId,
        string sku,
        string barcode,
        decimal priceAdjustment,
        decimal costAdjustment,
        CancellationToken cancellationToken)
    {
        var businessId = businessScope.RequireBusinessId();
        var variant = await dbContext.ProductVariants
            .FirstOrDefaultAsync(candidate => candidate.Id == variantId && candidate.BusinessId == businessId, cancellationToken)
            ?? throw new KeyNotFoundException($"Variant with ID {variantId} not found");

        variant.Sku = sku;
        variant.Barcode = barcode;
        variant.PriceAdjustment = priceAdjustment;
        variant.CostAdjustment = costAdjustment;
        variant.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<ProductVariantDeleteData?> GetDeleteDataAsync(
        long variantId,
        CancellationToken cancellationToken)
    {
        var businessId = businessScope.RequireBusinessId();
        var variant = await dbContext.ProductVariants
            .AsNoTracking()
            .Where(candidate => candidate.Id == variantId && candidate.BusinessId == businessId)
            .Select(candidate => new ProductVariantDeleteData(
                candidate.SaleItems.Any(),
                candidate.PurchaseOrderItems.Any(),
                candidate.StockTransferItems.Any(),
                candidate.StockAdjustmentLines.Any()))
            .FirstOrDefaultAsync(cancellationToken);

        return variant;
    }

    public async Task DeleteAsync(long variantId, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var businessId = businessScope.RequireBusinessId();
            var variant = await dbContext.ProductVariants
                .Include(candidate => candidate.Inventories)
                .FirstOrDefaultAsync(candidate => candidate.Id == variantId && candidate.BusinessId == businessId, cancellationToken)
                ?? throw new KeyNotFoundException($"Variant with ID {variantId} not found");

            dbContext.Inventories.RemoveRange(variant.Inventories);
            dbContext.ProductVariants.Remove(variant);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
