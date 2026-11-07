namespace RetailPOS.Application.Inventory.ReadModels;

public sealed record InventoryValuationReadModel(
    string LocationName,
    string LocationType,
    int TotalItems,
    decimal TotalQuantity,
    decimal TotalCostValue,
    decimal TotalRetailValue,
    IReadOnlyList<CategoryValuationReadModel> CategoryBreakdown);

public sealed record CategoryValuationReadModel(
    string CategoryName,
    int ProductCount,
    decimal TotalQuantity,
    decimal TotalCostValue,
    decimal TotalRetailValue);
