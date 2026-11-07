namespace RetailPOS.Application.PurchaseOrders.ReadModels;

public sealed record PurchaseOrderReadModel(
    long Id,
    string PoNumber,
    long SupplierId,
    string SupplierName,
    long? WarehouseId,
    string WarehouseName,
    DateTime OrderDate,
    DateTime? ExpectedDelivery,
    decimal TotalAmount,
    string Status,
    string? Notes,
    string? RejectionReason,
    long? CreatedBy,
    string? CreatedByName,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    long? LatestGrnId,
    IReadOnlyList<PurchaseOrderItemReadModel> Items,
    long? OutletId = null,
    string? OutletName = null)
{
    public string LocationType => OutletId.HasValue ? "outlet" : "warehouse";

    public long LocationId => OutletId ?? WarehouseId ?? 0;

    public string LocationName => OutletId.HasValue ? OutletName ?? string.Empty : WarehouseName;
}

public sealed record PurchaseOrderItemReadModel(
    long Id,
    long VariantId,
    string ProductName,
    string VariantName,
    string? ProductCode,
    string? Sku,
    string? VariantAttributes,
    decimal Quantity,
    string? Unit,
    decimal UnitPrice,
    decimal Discount,
    decimal Tax,
    string? UomUnitCode = null,
    string? UomLabel = null,
    decimal? UomEnteredQuantity = null,
    decimal? UomEnteredUnitPrice = null,
    long? UomOptionId = null);
