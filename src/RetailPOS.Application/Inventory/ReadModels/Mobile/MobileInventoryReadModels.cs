namespace RetailPOS.Application.Inventory.ReadModels.Mobile;

public sealed record MobileInventoryCriteria(
    long OutletId,
    string? Search,
    long? ProductId,
    long? VariantId,
    bool LowStockOnly,
    int Page,
    int PageSize);

public sealed record MobileInventoryPageReadModel(
    IReadOnlyList<MobileInventoryItemReadModel> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record MobileInventoryItemReadModel(
    long InventoryId,
    long ProductId,
    long VariantId,
    string ProductName,
    string? ProductCode,
    string VariantName,
    string? Sku,
    string? Barcode,
    string Attributes,
    long OutletId,
    string OutletName,
    decimal Quantity,
    int ReorderLevel,
    bool IsLowStock);
