using RetailPOS.Core.Entities;

namespace RetailPOS.Infrastructure.Repositories;

/// <summary>
/// Repository interface for Product operations
/// </summary>
public interface IProductRepository
{
    Task<Product?> GetByIdAsync(long id, bool includeVariants = true);
    Task<Product?> GetBySkuAsync(string sku);
    Task<Product?> GetByBarcodeAsync(string barcode);
    Task<IEnumerable<Product>> GetAllAsync();
    Task<(IEnumerable<Product> Products, int TotalCount)> SearchAsync(
        string? searchQuery,
        long? categoryId,
        bool? isActive,
        bool? hasVariants,
        decimal? minPrice,
        decimal? maxPrice,
        int pageNumber,
        int pageSize,
        string sortBy,
        string sortOrder);
    Task<IEnumerable<Product>> GetByCategoryAsync(long categoryId);
    Task<Product> CreateAsync(Product product);
    Task UpdateAsync(Product product);
    Task DeleteAsync(long id);
    Task<bool> ExistsAsync(string name, long? excludeId = null);
    Task<bool> SkuExistsAsync(string sku, long? excludeProductId = null);
    Task<bool> BarcodeExistsAsync(string barcode, long? excludeProductId = null);
    Task<int> GetTotalStockAsync(long productId);
}
