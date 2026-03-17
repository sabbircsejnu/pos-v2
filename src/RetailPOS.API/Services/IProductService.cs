using RetailPOS.API.DTOs.Product;

namespace RetailPOS.API.Services;

/// <summary>
/// Service interface for Product operations
/// </summary>
public interface IProductService
{
    Task<ProductDto?> GetByIdAsync(long id);
    Task<ProductDto?> GetBySkuAsync(string sku);
    Task<ProductDto?> GetByBarcodeAsync(string barcode);
    Task<IEnumerable<ProductDto>> GetAllAsync();
    Task<ProductListDto> SearchAsync(ProductSearchDto searchDto);
    Task<IEnumerable<ProductDto>> GetByCategoryAsync(long categoryId);
    Task<ProductDto> CreateAsync(CreateProductDto createDto);
    Task<ProductDto> UpdateAsync(long id, UpdateProductDto updateDto);
    Task DeleteAsync(long id);
    Task<string> GenerateSkuAsync(string productName);
    Task<IEnumerable<ProductVariantSearchDto>> SearchVariantsAsync(string query, int pageNumber, int pageSize);
}
