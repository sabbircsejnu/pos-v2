using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Repositories;

/// <summary>
/// Repository implementation for ProductVariant operations
/// </summary>
public class ProductVariantRepository : IProductVariantRepository
{
    private readonly RetailPOSDbContext _context;

    public ProductVariantRepository(RetailPOSDbContext context)
    {
        _context = context;
    }

    public async Task<ProductVariant?> GetByIdAsync(long id)
    {
        return await _context.ProductVariants
            .Include(v => v.Product)
            .ThenInclude(p => p.Category)
            .FirstOrDefaultAsync(v => v.Id == id);
    }

    public async Task<ProductVariant?> GetBySkuAsync(string sku)
    {
        return await _context.ProductVariants
            .Include(v => v.Product)
            .FirstOrDefaultAsync(v => v.Sku == sku);
    }

    public async Task<ProductVariant?> GetByBarcodeAsync(string barcode)
    {
        return await _context.ProductVariants
            .Include(v => v.Product)
            .FirstOrDefaultAsync(v => v.Barcode == barcode);
    }

    public async Task<IEnumerable<ProductVariant>> GetByProductIdAsync(long productId)
    {
        return await _context.ProductVariants
            .Where(v => v.ProductId == productId)
            .OrderBy(v => v.Id)
            .ToListAsync();
    }

    public async Task<ProductVariant> CreateAsync(ProductVariant variant)
    {
        variant.CreatedAt = DateTime.UtcNow;
        variant.UpdatedAt = DateTime.UtcNow;

        _context.ProductVariants.Add(variant);
        await _context.SaveChangesAsync();

        return variant;
    }

    public async Task UpdateAsync(ProductVariant variant)
    {
        variant.UpdatedAt = DateTime.UtcNow;

        _context.ProductVariants.Update(variant);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(long id)
    {
        var variant = await _context.ProductVariants.FindAsync(id);
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

        var query = _context.ProductVariants.Where(v => v.Sku == sku);

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

        var query = _context.ProductVariants.Where(v => v.Barcode == barcode);

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
        var queryLower = query?.ToLower() ?? "";
        
        var variants = await _context.ProductVariants
            .Include(v => v.Product)
            .Where(v => v.Product.IsActive &&
                        (string.IsNullOrEmpty(queryLower) ||
                         v.Product.Name.ToLower().Contains(queryLower) ||
                         v.Name.ToLower().Contains(queryLower) ||
                         (v.Sku != null && v.Sku.ToLower().Contains(queryLower)) ||
                         (v.Barcode != null && v.Barcode.ToLower().Contains(queryLower))))
            .OrderBy(v => v.Product.Name)
            .ThenBy(v => v.Name)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
            
        return variants;
    }
}
