using System.Text.Json;

namespace RetailPOS.API.DTOs.Mobile;

public sealed record MobileProductCategoryPageDto(
    IReadOnlyList<MobileProductCategoryDto> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record MobileProductCategoryDto(
    long Id,
    string Name,
    long? ParentCategoryId);

public sealed record MobileProductCataloguePageDto(
    IReadOnlyList<MobileProductSummaryDto> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record MobileProductSummaryDto(
    long Id,
    string Name,
    string? Description,
    string? MainProductCode,
    string? Sku,
    string? ProductBarcode,
    long CategoryId,
    string CategoryName,
    string Status,
    bool HasVariants,
    int VariantCount);

public sealed record MobileProductDetailDto(
    long Id,
    string Name,
    string? Description,
    string? MainProductCode,
    string? Sku,
    string? ProductBarcode,
    long CategoryId,
    string CategoryName,
    string Status,
    bool HasVariants);

public sealed record MobileProductVariantPageDto(
    IReadOnlyList<MobileProductVariantDto> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record MobileProductVariantDto(
    long Id,
    long ProductId,
    string Name,
    string Sku,
    string? Barcode,
    JsonElement Attributes);

public sealed record MobileProductBarcodeResolutionDto(
    string MatchType,
    MobileProductDetailDto Product,
    MobileProductVariantDto? Variant);
