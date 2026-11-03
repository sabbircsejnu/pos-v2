using RetailPOS.Core.Entities;

namespace RetailPOS.Application.Products.ReadModels;

public sealed record ProductUpdateSnapshot(
    string? Sku,
    string? Barcode,
    string? ProductCode);

public sealed record ProductUpdateValues(
    string Name,
    string? Description,
    string? ProductCode,
    string? Sku,
    string? Barcode,
    long CategoryId,
    decimal BasePrice,
    decimal CostPrice,
    decimal? TaxRate,
    string? TaxClassification,
    ProductStatus Status);
