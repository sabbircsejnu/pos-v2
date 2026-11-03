namespace RetailPOS.Application.Products.ReadModels;

public sealed record ProductVariantSearchCriteria(
    string Query,
    int PageNumber,
    int PageSize,
    long? LocationId,
    string? LocationType);

public sealed record ProductVariantSearchReadModel(
    long Id,
    long ProductId,
    string ProductName,
    string? ProductCode,
    string Name,
    string? Sku,
    string? Barcode,
    string? Attributes,
    decimal FinalPrice,
    decimal CostPrice,
    int? StockQuantity,
    string? PrimaryImageThumb);
