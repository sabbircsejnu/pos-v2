namespace RetailPOS.Application.Inventory.StockTransfers;

public sealed record StockTransferCreateItem(
    long VariantId,
    int Quantity,
    int? RequestedQuantity,
    int? TransferQuantity,
    decimal UnitCost,
    string? Remarks);

public sealed record StockTransferCreateInput(
    string? TransferType,
    long? RelatedRequisitionId,
    long FromLocationId,
    string FromLocationType,
    long ToLocationId,
    string ToLocationType,
    DateTime TransferDate,
    string? Notes,
    IReadOnlyList<StockTransferCreateItem> Items);
