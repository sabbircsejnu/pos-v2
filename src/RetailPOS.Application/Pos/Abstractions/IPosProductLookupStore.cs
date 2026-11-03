using RetailPOS.Application.Pos.ReadModels;

namespace RetailPOS.Application.Pos.Abstractions;

public interface IPosProductLookupStore
{
    Task<PosProductLookupVariantReadModel?> FindVariantByBarcodeAsync(
        string barcode,
        CancellationToken cancellationToken);

    Task<PosProductLookupVariantReadModel?> FindFirstActiveProductVariantByBarcodeAsync(
        string barcode,
        CancellationToken cancellationToken);

    Task<PosProductLookupVariantReadModel?> FindVariantBySkuAsync(
        string sku,
        CancellationToken cancellationToken);

    Task<PosProductLookupVariantReadModel?> FindVariantByIdAsync(
        long variantId,
        CancellationToken cancellationToken);

    Task<int> GetOutletStockAsync(
        long variantId,
        long outletId,
        CancellationToken cancellationToken);
}
