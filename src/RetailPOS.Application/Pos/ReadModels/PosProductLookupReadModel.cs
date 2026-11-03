namespace RetailPOS.Application.Pos.ReadModels;

public enum PosProductLookupMode
{
    Barcode,
    Sku,
    Variant
}

public sealed record PosProductLookupVariantReadModel(
    long VariantId,
    long ProductId,
    string ProductName,
    string VariantName,
    string? Barcode,
    string Sku,
    string? ImageUrl,
    decimal BasePrice,
    decimal? TaxRate,
    bool IsActive,
    string? TaxClassification = null);

public sealed record PosLookupPriceReadModel(
    decimal FinalPrice,
    string? AppliedRuleName);

public sealed record PosProductLookupReadModel(
    long VariantId,
    long ProductId,
    string ProductName,
    string VariantName,
    string? Barcode,
    string Sku,
    string? ImageUrl,
    decimal BasePrice,
    decimal EffectivePrice,
    decimal TaxRate,
    string? AppliedRuleName,
    int StockQty,
    bool InStock,
    bool StockFromCache,
    string TaxClassification = "standard",
    bool PricesIncludeTax = false);
