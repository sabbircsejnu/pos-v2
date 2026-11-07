namespace RetailPOS.Application.Grns.ReadModels;

public sealed record PendingGrnPurchaseOrderReadModel(
    long Id,
    long SupplierId,
    string SupplierName,
    long? WarehouseId,
    string WarehouseName,
    DateTime OrderDate,
    decimal TotalAmount,
    string Status,
    IReadOnlyList<PendingGrnPurchaseOrderItemReadModel> Items,
    long? OutletId = null,
    string? OutletName = null)
{
    public string LocationType => OutletId.HasValue ? "outlet" : "warehouse";

    public long LocationId => OutletId ?? WarehouseId ?? 0;

    public string LocationName => OutletId.HasValue ? OutletName ?? string.Empty : WarehouseName;
}

public sealed record PendingGrnPurchaseOrderItemReadModel(
    long Id,
    long VariantId,
    string ProductName,
    string? ProductCode,
    string VariantName,
    string? VariantAttributes,
    decimal Quantity,
    decimal UnitPrice,
    string? PrimaryImageThumb);
