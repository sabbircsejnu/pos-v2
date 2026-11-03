namespace RetailPOS.Application.Inventory.ReadModels;

public sealed record InventoryReadModel(
    long Id,
    long ProductId,
    long ProductVariantId,
    string ProductName,
    string? ProductCode,
    string VariantName,
    string? VariantAttributes,
    string? PrimaryImageThumb,
    string? Sku,
    string? Barcode,
    long? OutletId,
    string? OutletName,
    long? WarehouseId,
    string? WarehouseName,
    string LocationType,
    int Quantity,
    int ReorderLevel,
    string? BatchNumber,
    DateTime? ExpiryDate,
    decimal CostPrice,
    decimal RetailPrice);
