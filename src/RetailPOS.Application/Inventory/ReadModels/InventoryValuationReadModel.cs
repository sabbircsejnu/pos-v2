namespace RetailPOS.Application.Inventory.ReadModels;

public sealed record InventoryValuationReadModel(
    string LocationName,
    string LocationType,
    int TotalItems,
    int TotalQuantity,
    decimal TotalCostValue,
    decimal TotalRetailValue,
    IReadOnlyList<CategoryValuationReadModel> CategoryBreakdown);

public sealed record CategoryValuationReadModel(
    string CategoryName,
    int ProductCount,
    int TotalQuantity,
    decimal TotalCostValue,
    decimal TotalRetailValue);
