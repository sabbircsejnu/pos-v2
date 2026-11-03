using RetailPOS.Core.Entities;

namespace RetailPOS.Application.Products.ReadModels;

public sealed record MobileProductCategoryPageReadModel(
    IReadOnlyList<MobileProductCategoryReadModel> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record MobileProductCategoryReadModel(
    long Id,
    string Name,
    long? ParentCategoryId);

public sealed record MobileProductCatalogueCriteria(
    string? Search,
    long? CategoryId,
    ProductStatus Status,
    string? AttributeName,
    string? AttributeValue,
    int Page,
    int PageSize);

public sealed record MobileProductCataloguePageReadModel(
    IReadOnlyList<MobileProductSummaryReadModel> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record MobileProductSummaryReadModel(
    long Id,
    string Name,
    string? Description,
    string? ProductCode,
    string? Sku,
    string? ProductBarcode,
    long CategoryId,
    string CategoryName,
    string Status,
    bool HasVariants,
    int VariantCount);

public sealed record MobileProductReadModel(
    long Id,
    string Name,
    string? Description,
    string? ProductCode,
    string? Sku,
    string? ProductBarcode,
    long CategoryId,
    string CategoryName,
    string Status,
    bool HasVariants);

public sealed record MobileProductVariantPageReadModel(
    IReadOnlyList<MobileProductVariantReadModel> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record MobileProductVariantReadModel(
    long Id,
    long ProductId,
    string Name,
    string Sku,
    string? Barcode,
    string Attributes);

public sealed record MobileProductBarcodeMatchReadModel(
    MobileProductReadModel Product,
    MobileProductVariantReadModel? Variant,
    string MatchType);
