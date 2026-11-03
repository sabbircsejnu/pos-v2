using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Repositories;

/// <summary>
/// Repository implementation for ProductVariant operations
/// </summary>
public class ProductVariantRepository : IProductVariantRepository
{
    private readonly RetailPOSDbContext _context;
    private readonly IBusinessScope? _businessScope;

    public ProductVariantRepository(RetailPOSDbContext context, IBusinessScope? businessScope = null)
    {
        _context = context;
        _businessScope = businessScope;
    }

    /// <summary>Variants of the caller's Business. Without a scope (tests, background work) nothing is filtered.</summary>
    private IQueryable<ProductVariant> ScopedVariants()
    {
        if (_businessScope is null)
            return _context.ProductVariants;

        var businessId = _businessScope.RequireBusinessId();
        return _context.ProductVariants.Where(v => v.BusinessId == businessId);
    }

    public async Task<ProductVariant?> GetByIdAsync(long id)
    {
        return await ScopedVariants()
            .Include(v => v.Product)
            .ThenInclude(p => p.Category)
            .FirstOrDefaultAsync(v => v.Id == id);
    }

    public async Task<ProductVariant?> GetBySkuAsync(string sku)
    {
        return await ScopedVariants()
            .Include(v => v.Product)
            .FirstOrDefaultAsync(v => v.Sku == sku);
    }

    public async Task<ProductVariant?> GetByBarcodeAsync(string barcode)
    {
        return await ScopedVariants()
            .Include(v => v.Product)
            .FirstOrDefaultAsync(v => v.Barcode == barcode);
    }

    public async Task<IEnumerable<ProductVariant>> GetByProductIdAsync(long productId)
    {
        return await ScopedVariants()
            .Where(v => v.ProductId == productId)
            .OrderBy(v => v.Id)
            .ToListAsync();
    }

    public async Task<ProductVariant> CreateAsync(ProductVariant variant)
    {
        if (_businessScope is not null)
        {
            // A variant always takes the Business of its product; a product of another Business is "not found".
            var businessId = _businessScope.RequireBusinessId();
            var ownsProduct = await _context.Products.AnyAsync(p => p.Id == variant.ProductId && p.BusinessId == businessId);
            if (!ownsProduct)
                throw new KeyNotFoundException($"Product with ID {variant.ProductId} not found");
            variant.BusinessId = businessId;
        }

        variant.CreatedAt = DateTime.UtcNow;
        variant.UpdatedAt = DateTime.UtcNow;

        _context.ProductVariants.Add(variant);
        await _context.SaveChangesAsync();

        return variant;
    }

    public async Task UpdateAsync(ProductVariant variant)
    {
        if (_businessScope is not null && variant.BusinessId != _businessScope.RequireBusinessId())
            throw new KeyNotFoundException($"Variant with ID {variant.Id} not found");

        variant.UpdatedAt = DateTime.UtcNow;

        _context.ProductVariants.Update(variant);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(long id)
    {
        var variant = await ScopedVariants().FirstOrDefaultAsync(v => v.Id == id);
        if (variant != null)
        {
            _context.ProductVariants.Remove(variant);
            await _context.SaveChangesAsync();
        }
    }

    public async Task<bool> SkuExistsAsync(string sku, long? excludeVariantId = null)
    {
        if (string.IsNullOrWhiteSpace(sku))
            return false;

        var query = ScopedVariants().Where(v => v.Sku == sku);

        if (excludeVariantId.HasValue)
        {
            query = query.Where(v => v.Id != excludeVariantId.Value);
        }

        return await query.AnyAsync();
    }

    public async Task<bool> BarcodeExistsAsync(string barcode, long? excludeVariantId = null)
    {
        if (string.IsNullOrWhiteSpace(barcode))
            return false;

        var query = ScopedVariants().Where(v => v.Barcode == barcode);

        if (excludeVariantId.HasValue)
        {
            query = query.Where(v => v.Id != excludeVariantId.Value);
        }

        return await query.AnyAsync();
    }

    public async Task<int> GetStockAsync(long variantId)
    {
        // TODO: Implement when Inventory entity is available
        // var stock = await _context.Inventory
        //     .Where(i => i.ProductVariantId == variantId)
        //     .SumAsync(i => i.QuantityOnHand);

        return 0; // Placeholder until Inventory module is implemented
    }

    public async Task<IEnumerable<ProductVariant>> SearchAsync(string query, int pageNumber, int pageSize)
    {
        // Resolved before the try block so a missing Business context is an error, never an empty result.
        var scopedVariants = ScopedVariants();
        var businessId = _businessScope?.RequireBusinessId();

        var queryLower = query?.Trim().ToLower() ?? "";
        var hasQuery = !string.IsNullOrWhiteSpace(queryLower);
        var compactQuery = queryLower
            .Replace(" ", string.Empty)
            .Replace("-", string.Empty)
            .Replace("_", string.Empty)
            .Replace("/", string.Empty)
            .Replace(".", string.Empty);
        var likePattern = $"%{queryLower}%";
        var attributeMatchIds = new List<long>();

        if (hasQuery)
        {
            if (_context.Database.IsNpgsql())
            {
                // Attributes is stored as jsonb in PostgreSQL; cast to text for partial matching.
                attributeMatchIds = businessId.HasValue
                    ? await _context.Database
                        .SqlQueryRaw<long>(
                            "SELECT id FROM product_variants WHERE business_id = {0} AND attributes::text ILIKE {1}",
                            businessId.Value,
                            likePattern)
                        .ToListAsync()
                    : await _context.Database
                        .SqlQueryRaw<long>(
                            "SELECT id FROM product_variants WHERE attributes::text ILIKE {0}",
                            likePattern)
                        .ToListAsync();
            }
            else
            {
                attributeMatchIds = await scopedVariants
                    .AsNoTracking()
                    .Where(v => v.Attributes != null && v.Attributes.ToLower().Contains(queryLower))
                    .Select(v => v.Id)
                    .ToListAsync();
            }
        }

        try
        {
            var variants = await scopedVariants
            .AsNoTracking()
            .Include(v => v.Product)
                .ThenInclude(p => p.Category)
            .Include(v => v.Product)
                .ThenInclude(p => p.Images.Where(i => i.IsPrimary))
            .Where(v => v.Product.Status == ProductStatus.Active &&
                        (!hasQuery ||
                         ((v.Product.ProductCode ?? string.Empty).ToLower() == queryLower) ||
                         ((v.Product.ProductCode ?? string.Empty).ToLower().Contains(queryLower)) ||
                         ((v.Product.ProductCode ?? string.Empty)
                             .ToLower()
                             .Replace(" ", string.Empty)
                             .Replace("-", string.Empty)
                             .Replace("_", string.Empty)
                             .Replace("/", string.Empty)
                             .Replace(".", string.Empty)
                             .Contains(compactQuery)) ||
                         ((v.Product.Name ?? string.Empty).ToLower().Contains(queryLower)) ||
                         ((v.Product.Description ?? string.Empty).ToLower().Contains(queryLower)) ||
                         ((v.Name ?? string.Empty).ToLower().Contains(queryLower)) ||
                         ((v.Sku ?? string.Empty).ToLower().Contains(queryLower)) ||
                         ((v.Barcode ?? string.Empty).ToLower().Contains(queryLower)) ||
                         ((v.Product.Barcode ?? string.Empty).ToLower().Contains(queryLower)) ||
                         attributeMatchIds.Contains(v.Id)))
            .OrderBy(v => v.Product.Name)
            .ThenBy(v => v.Name)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

            return variants;
        }
        catch(Exception)
        {
            return new List<ProductVariant>(); // Return an empty list in case of an exception
        }
        
    }
}
