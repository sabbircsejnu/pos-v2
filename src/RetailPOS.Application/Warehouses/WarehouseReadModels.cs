namespace RetailPOS.Application.Warehouses;

public sealed record WarehouseReadModel(
    long Id,
    string Name,
    string Address,
    int? Capacity,
    long? ManagerId,
    string? ManagerName,
    int PurchaseOrderCount,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record WarehouseStatsReadModel(
    long Id,
    string Name,
    int? Capacity,
    int PurchaseOrderCount,
    int CurrentStock,
    decimal CapacityUtilization);
