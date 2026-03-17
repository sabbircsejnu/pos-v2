using RetailPOS.Core.Entities;

namespace RetailPOS.Infrastructure.Repositories;

/// <summary>
/// Repository interface for ProductVariant operations
/// </summary>
public interface IProductVariantRepository
{
    Task<ProductVariant?> GetByIdAsync(long id);
    Task<ProductVariant?> GetBySkuAsync(string sku);
    Task<ProductVariant?> GetByBarcodeAsync(string barcode);
    Task<IEnumerable<ProductVariant>> GetByProductIdAsync(long productId);
    Task<ProductVariant> CreateAsync(ProductVariant variant);
    Task UpdateAsync(ProductVariant variant);
    Task DeleteAsync(long id);
    Task<bool> SkuExistsAsync(string sku, long? excludeVariantId = null);
    Task<bool> BarcodeExistsAsync(string barcode, long? excludeVariantId = null);
    Task<int> GetStockAsync(long variantId);
    Task<IEnumerable<ProductVariant>> SearchAsync(string query, int pageNumber, int pageSize);
}
