namespace RetailPOS.API.Services;

public interface IProductIdentifierService
{
    Task<string> ResolveVariantSkuAsync(
        string? requestedSku,
        string mainProductCode,
        string? sizeCode,
        string? attributeCode,
        long? excludeVariantId = null);

    Task<string> ResolveVariantBarcodeAsync(
        string? requestedBarcode,
        long categoryId,
        long? excludeVariantId = null);

    (string SizeCode, string AttributeCode) ExtractCodesFromVariantText(string? variantName, string? attributesJson);

    (string SizeCode, string AttributeCode) ExtractCodesFromOptions(IEnumerable<(string VariationName, string OptionName)> options);
}
