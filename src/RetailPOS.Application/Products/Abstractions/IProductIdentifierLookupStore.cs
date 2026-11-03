namespace RetailPOS.Application.Products.Abstractions;

public interface IProductIdentifierLookupStore
{
    Task<bool> SkuExistsAsync(
        string sku,
        long? excludeVariantId,
        CancellationToken cancellationToken);

    Task<bool> BarcodeExistsAsync(
        string barcode,
        long? excludeVariantId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> GetBarcodeCandidatesAsync(
        string prefix,
        CancellationToken cancellationToken);
}
