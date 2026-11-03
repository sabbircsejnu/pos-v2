namespace RetailPOS.Application.Inventory.ReadModels.Alerts;

public sealed record LowStockReadModel(
    long InventoryId,
    long ProductVariantId,
    long LocationId,
    string ProductName,
    string VariantName,
    string? VariantAttributes,
    string? PrimaryImageThumb,
    string? Sku,
    int CurrentQuantity,
    int ReorderLevel,
    string LocationName,
    string LocationType);
