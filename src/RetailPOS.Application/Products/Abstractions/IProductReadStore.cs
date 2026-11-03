using RetailPOS.Application.Products.ReadModels;

namespace RetailPOS.Application.Products.Abstractions;

public interface IProductReadStore
{
    Task<ProductReadModel?> FindByIdAsync(long productId, CancellationToken cancellationToken);

    Task<ProductReadModel?> FindBySkuAsync(string sku, CancellationToken cancellationToken);

    Task<ProductReadModel?> FindByBarcodeAsync(string barcode, CancellationToken cancellationToken);

    Task<IReadOnlyList<ProductReadModel>> ListAllAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<ProductReadModel>> ListByCategoryAsync(
        long categoryId,
        CancellationToken cancellationToken);

    Task<ProductSearchReadModel> SearchAsync(
        ProductSearchCriteria criteria,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ProductVariantSearchReadModel>> SearchVariantsAsync(
        ProductVariantSearchCriteria criteria,
        CancellationToken cancellationToken);
}
