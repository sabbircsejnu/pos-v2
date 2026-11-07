namespace RetailPOS.Application.Inventory.StockTransfers;

public sealed record StockTransferReceiveInput(
    IReadOnlyList<StockTransferReceiveItemInput>? Items,
    string? Notes);

public sealed record StockTransferReceiveItemInput(
    long VariantId,
    decimal AcceptedQuantity,
    decimal RejectedQuantity,
    string? Remarks,
    decimal MissingQuantity = 0,
    decimal DamagedQuantity = 0);

public sealed record StockTransferReceiveResult(
    long TransferId,
    string ToLocationType,
    long ToLocationId,
    IReadOnlyList<long> VariantIds);
