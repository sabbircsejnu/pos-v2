using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Abstractions;
using RetailPOS.Application.Products.Abstractions;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Products;

/// <summary>SKU and barcode uniqueness is per Business, so one Business can never learn (or be blocked by) another's identifiers.</summary>
public sealed class ProductIdentifierLookupStore(RetailPOSDbContext dbContext, IBusinessScope businessScope)
    : IProductIdentifierLookupStore
{
    public async Task<bool> SkuExistsAsync(
        string sku,
        long? excludeVariantId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sku))
        {
            return false;
        }

        var businessId = businessScope.RequireBusinessId();
        var query = dbContext.ProductVariants
            .AsNoTracking()
            .Where(variant => variant.BusinessId == businessId && variant.Sku == sku);
        if (excludeVariantId.HasValue)
        {
            query = query.Where(variant => variant.Id != excludeVariantId.Value);
        }

        return await query.AnyAsync(cancellationToken);
    }

    public async Task<bool> BarcodeExistsAsync(
        string barcode,
        long? excludeVariantId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(barcode))
        {
            return false;
        }

        var businessId = businessScope.RequireBusinessId();
        var query = dbContext.ProductVariants
            .AsNoTracking()
            .Where(variant => variant.BusinessId == businessId && variant.Barcode == barcode);
        if (excludeVariantId.HasValue)
        {
            query = query.Where(variant => variant.Id != excludeVariantId.Value);
        }

        return await query.AnyAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetBarcodeCandidatesAsync(
        string prefix,
        CancellationToken cancellationToken)
    {
        var businessId = businessScope.RequireBusinessId();
        return await dbContext.ProductVariants
            .AsNoTracking()
            .Where(variant =>
                variant.BusinessId == businessId
                && variant.Barcode != null
                && variant.Barcode.StartsWith(prefix)
                && variant.Barcode.Length == 13)
            .Select(variant => variant.Barcode!)
            .ToListAsync(cancellationToken);
    }
}
