namespace RetailPOS.Application.Products.ReadModels;

public sealed record ProductReadModel(
    long Id,
    string Name,
    string? Description,
    string? ProductCode,
    string? Sku,
    string? Barcode,
    long CategoryId,
    string CategoryName,
    decimal BasePrice,
    decimal CostPrice,
    decimal? TaxRate,
    string? TaxClassification,
    bool HasVariants,
    string? PrimaryImageThumb,
    string? PrimaryImageMedium,
    string Status,
    bool IsActive,
    int VariantCount,
    int TotalStock,
    IReadOnlyList<ProductVariantReadModel> Variants,
    DateTime CreatedAt,
    DateTime UpdatedAt)
{
    public string StockStatus { get; init; } = "unavailable";
}

public sealed record ProductVariantReadModel(
    long Id,
    long ProductId,
    string? ProductCode,
    string Name,
    string? Sku,
    string? Barcode,
    string? Attributes,
    decimal PriceAdjustment,
    decimal FinalPrice,
    DateTime CreatedAt,
    DateTime UpdatedAt);
