namespace RetailPOS.Application.Products.Abstractions;

public interface IProductIdentifierResolver
{
    Task<string> ResolveVariantSkuAsync(
        string? requestedSku,
        string mainProductCode,
        string? sizeCode,
        string? attributeCode,
        long? excludeVariantId = null,
        CancellationToken cancellationToken = default);

    Task<string> ResolveVariantBarcodeAsync(
        string? requestedBarcode,
        long categoryId,
        long? excludeVariantId = null,
        CancellationToken cancellationToken = default);

    (string SizeCode, string AttributeCode) ExtractCodesFromVariantText(
        string? variantName,
        string? attributesJson);

    (string SizeCode, string AttributeCode) ExtractCodesFromOptions(
        IEnumerable<(string VariationName, string OptionName)> options);
}
