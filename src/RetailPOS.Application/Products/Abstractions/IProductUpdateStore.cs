using RetailPOS.Application.Products.ReadModels;

namespace RetailPOS.Application.Products.Abstractions;

public interface IProductUpdateStore
{
    Task<ProductUpdateSnapshot?> FindForUpdateAsync(
        long productId,
        CancellationToken cancellationToken);

    Task<bool> NameExistsAsync(
        string name,
        long excludedProductId,
        CancellationToken cancellationToken);

    Task<bool> SkuExistsAsync(
        string sku,
        long excludedProductId,
        CancellationToken cancellationToken);

    Task<bool> BarcodeExistsAsync(
        string barcode,
        long excludedProductId,
        CancellationToken cancellationToken);

    Task<bool> ProductCodeExistsAsync(
        string productCode,
        long excludedProductId,
        CancellationToken cancellationToken);

    Task UpdateAsync(
        long productId,
        ProductUpdateValues values,
        CancellationToken cancellationToken);
}
