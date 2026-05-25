using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Repositories;

/// <summary>
/// Repository implementation for Product operations
/// </summary>
public class ProductRepository : IProductRepository
{
    private readonly RetailPOSDbContext _context;

    public ProductRepository(RetailPOSDbContext context)
    {
        _context = context;
    }

    public async Task<Product?> GetByIdAsync(long id, bool includeVariants = true)
    {
        var query = _context.Products
            .Include(p => p.Category)
            .Include(p => p.Images.Where(i => i.IsPrimary))
            .AsQueryable();

        if (includeVariants)
        {
            query = query.Include(p => p.ProductVariants);
        }

        return await query.FirstOrDefaultAsync(p => p.Id == id);
    }

    public async Task<Product?> GetBySkuAsync(string sku)
    {
        return await _context.Products
            .Include(p => p.Category)
            .Include(p => p.ProductVariants)
            .Include(p => p.Images.Where(i => i.IsPrimary))
            .FirstOrDefaultAsync(p => p.Sku == sku);
    }

    public async Task<Product?> GetByBarcodeAsync(string barcode)
    {
        return await _context.Products
            .Include(p => p.Category)
            .Include(p => p.ProductVariants)
            .Include(p => p.Images.Where(i => i.IsPrimary))
            .FirstOrDefaultAsync(p => p.Barcode == barcode);
    }

    public async Task<IEnumerable<Product>> GetAllAsync()
    {
        return await _context.Products
            .Include(p => p.Category)
            .Include(p => p.ProductVariants)
            .Include(p => p.Images.Where(i => i.IsPrimary))
            .OrderBy(p => p.Name)
            .ToListAsync();
    }

    public async Task<(IEnumerable<Product> Products, int TotalCount)> SearchAsync(
        string? searchQuery,
        long? categoryId,
        bool? isActive,
        bool? hasVariants,
        decimal? minPrice,
        decimal? maxPrice,
        int pageNumber,
        int pageSize,
        string sortBy,
        string sortOrder)
    {
        var query = _context.Products
            .Include(p => p.Category)
            .Include(p => p.ProductVariants)
            .Include(p => p.Images.Where(i => i.IsPrimary))
            .AsQueryable();

        // Apply filters
        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            var lowerSearchQuery = searchQuery.ToLower();
            query = query.Where(p =>
                p.Name.ToLower().Contains(lowerSearchQuery) ||
                (p.Description != null && p.Description.ToLower().Contains(lowerSearchQuery)) ||
                (p.Sku != null && p.Sku.ToLower().Contains(lowerSearchQuery)) ||
                (p.Barcode != null && p.Barcode.ToLower().Contains(lowerSearchQuery)));
        }

        if (categoryId.HasValue)
        {
            query = query.Where(p => p.CategoryId == categoryId.Value);
        }

        if (isActive.HasValue)
        {
            query = query.Where(p => p.IsActive == isActive.Value);
        }

        if (hasVariants.HasValue)
        {
            query = query.Where(p => p.HasVariants == hasVariants.Value);
        }

        if (minPrice.HasValue)
        {
            query = query.Where(p => p.BasePrice >= minPrice.Value);
        }

        if (maxPrice.HasValue)
        {
            query = query.Where(p => p.BasePrice <= maxPrice.Value);
        }

        // Get total count before pagination
        var totalCount = await query.CountAsync();

        // Apply sorting
        query = sortBy.ToLower() switch
        {
            "name" => sortOrder.ToLower() == "desc"
                ? query.OrderByDescending(p => p.Name)
                : query.OrderBy(p => p.Name),
            "price" => sortOrder.ToLower() == "desc"
                ? query.OrderByDescending(p => p.BasePrice)
                : query.OrderBy(p => p.BasePrice),
            "category" => sortOrder.ToLower() == "desc"
                ? query.OrderByDescending(p => p.Category.Name)
                : query.OrderBy(p => p.Category.Name),
            "createdat" => sortOrder.ToLower() == "desc"
                ? query.OrderByDescending(p => p.CreatedAt)
                : query.OrderBy(p => p.CreatedAt),
            _ => query.OrderBy(p => p.Name)
        };

        // Apply pagination
        var products = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (products, totalCount);
    }

    public async Task<IEnumerable<Product>> GetByCategoryAsync(long categoryId)
    {
        return await _context.Products
            .Include(p => p.Category)
            .Include(p => p.ProductVariants)
            .Include(p => p.Images.Where(i => i.IsPrimary))
            .Where(p => p.CategoryId == categoryId)
            .OrderBy(p => p.Name)
            .ToListAsync();
    }

    public async Task<Product> CreateAsync(Product product)
    {
        product.CreatedAt = DateTime.UtcNow;
        product.UpdatedAt = DateTime.UtcNow;

        _context.Products.Add(product);
        await _context.SaveChangesAsync();

        return product;
    }

    public async Task UpdateAsync(Product product)
    {
        product.UpdatedAt = DateTime.UtcNow;

        _context.Products.Update(product);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(long id)
    {
        var product = await _context.Products.FindAsync(id);
        if (product != null)
        {
            _context.Products.Remove(product);
            await _context.SaveChangesAsync();
        }
    }

    public async Task<bool> ExistsAsync(string name, long? excludeId = null)
    {
        var query = _context.Products
            .Where(p => p.Name.ToLower() == name.ToLower());

        if (excludeId.HasValue)
        {
            query = query.Where(p => p.Id != excludeId.Value);
        }

        return await query.AnyAsync();
    }

    public async Task<bool> SkuExistsAsync(string sku, long? excludeProductId = null)
    {
        if (string.IsNullOrWhiteSpace(sku))
            return false;

        var query = _context.Products.Where(p => p.Sku == sku);

        if (excludeProductId.HasValue)
        {
            query = query.Where(p => p.Id != excludeProductId.Value);
        }

        return await query.AnyAsync();
    }

    public async Task<bool> BarcodeExistsAsync(string barcode, long? excludeProductId = null)
    {
        if (string.IsNullOrWhiteSpace(barcode))
            return false;

        var query = _context.Products.Where(p => p.Barcode == barcode);

        if (excludeProductId.HasValue)
        {
            query = query.Where(p => p.Id != excludeProductId.Value);
        }

        return await query.AnyAsync();
    }

    public async Task<int> GetTotalStockAsync(long productId)
    {
        // Sum up stock from all inventory records for this product's variants
        var variantIds = await _context.ProductVariants
            .Where(v => v.ProductId == productId)
            .Select(v => v.Id)
            .ToListAsync();

        if (!variantIds.Any())
            return 0;

        // TODO: Implement when Inventory entity is available
        // var totalStock = await _context.Inventory
        //     .Where(i => variantIds.Contains(i.ProductVariantId))
        //     .SumAsync(i => i.QuantityOnHand);

        return 0; // Placeholder until Inventory module is implemented
    }
}
