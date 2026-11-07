namespace RetailPOS.Application.Inventory.ReadModels;

public sealed record InventoryLocationSummaryReadModel(
    long? OutletId,
    string? OutletName,
    long? WarehouseId,
    string? WarehouseName,
    string LocationType,
    int TotalProducts,
    decimal TotalQuantity,
    int LowStockCount,
    int OutOfStockCount,
    decimal TotalValue);
