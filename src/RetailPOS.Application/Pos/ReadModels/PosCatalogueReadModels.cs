namespace RetailPOS.Application.Pos.ReadModels;

/// <summary>Filters for the POS product browser. <see cref="OutletId"/> is already authorized by the caller.</summary>
public sealed record PosCatalogueCriteria(
    string? Search,
    long? CategoryId,
    long? OutletId,
    int Limit);

/// <summary>
/// One sellable variant for the POS product browser. <see cref="ListPrice"/> is a display hint
/// (base price plus variant adjustment); the authoritative unit price comes from POST /api/pos/cart-prices.
/// Cost data is never part of this model.
/// </summary>
public sealed record PosCatalogueItemReadModel(
    long VariantId,
    long ProductId,
    string ProductName,
    string VariantName,
    string? ProductCode,
    string Sku,
    string? Barcode,
    string? Attributes,
    long CategoryId,
    string? CategoryName,
    decimal ListPrice,
    string? PrimaryImageThumb,
    int? StockQty);

public sealed record PosCategoryReadModel(
    long Id,
    string Name,
    long? ParentCategoryId,
    int DisplayOrder);
