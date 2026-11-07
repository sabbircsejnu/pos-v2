namespace RetailPOS.Application.Grns.ReadModels;

public sealed record GrnCompletionScopeReadModel(
    long? BusinessId,
    long LocationId,
    string LocationType,
    string Status);

public sealed record GrnReadModel(
    long Id,
    long PurchaseOrderId,
    long SupplierId,
    string SupplierName,
    long? WarehouseId,
    string WarehouseName,
    DateTime ReceivedDate,
    string Status,
    string? Notes,
    string? SupplierInvoiceNumber,
    DateTime? SupplierInvoiceDate,
    long? CreatedBy,
    string? CreatedByName,
    DateTime CreatedAt,
    IReadOnlyList<GrnItemReadModel> Items,
    long? OutletId = null,
    string? OutletName = null)
{
    public string LocationType => OutletId.HasValue ? "outlet" : "warehouse";

    public long LocationId => OutletId ?? WarehouseId ?? 0;

    public string LocationName => OutletId.HasValue ? OutletName ?? string.Empty : WarehouseName;
}

public sealed record GrnItemReadModel(
    long Id,
    long PurchaseOrderItemId,
    long VariantId,
    string ProductName,
    string? ProductCode,
    string VariantName,
    string? VariantAttributes,
    decimal OrderedQuantity,
    decimal ReceivedQuantity,
    decimal UnitPrice,
    decimal UnitCost,
    string? Notes,
    decimal DamagedQuantity = 0m,
    decimal RejectedQuantity = 0m,
    int? AcceptedSealedPacks = null,
    decimal? AcceptedLooseQuantity = null,
    string? UomUnitCode = null,
    string? UomLabel = null,
    string? UomBaseUnitCode = null);
