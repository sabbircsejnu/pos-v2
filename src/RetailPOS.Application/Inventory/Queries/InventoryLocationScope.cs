namespace RetailPOS.Application.Inventory.Queries;

public sealed record InventoryLocationScope(
    IReadOnlyCollection<long> OutletIds,
    IReadOnlyCollection<long> WarehouseIds,
    long? SelectedOutletId = null,
    long? SelectedWarehouseId = null);
