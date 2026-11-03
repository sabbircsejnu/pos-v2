using RetailPOS.Application.Products.ReadModels;

namespace RetailPOS.Application.Products.Abstractions;

public interface IProductVariantMaintenanceStore
{
    Task<ProductVariantUpdateData?> GetUpdateDataAsync(long variantId, CancellationToken cancellationToken);

    Task SaveUpdateAsync(
        long variantId,
        string sku,
        string barcode,
        decimal priceAdjustment,
        decimal costAdjustment,
        CancellationToken cancellationToken);

    Task<ProductVariantDeleteData?> GetDeleteDataAsync(long variantId, CancellationToken cancellationToken);

    Task DeleteAsync(long variantId, CancellationToken cancellationToken);
}
