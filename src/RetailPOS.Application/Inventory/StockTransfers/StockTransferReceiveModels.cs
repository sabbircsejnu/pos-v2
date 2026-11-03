namespace RetailPOS.Application.Inventory.StockTransfers;

public sealed record StockTransferReceiveInput(
    IReadOnlyList<StockTransferReceiveItemInput>? Items,
    string? Notes);

public sealed record StockTransferReceiveItemInput(
    long VariantId,
    int AcceptedQuantity,
    int RejectedQuantity,
    string? Remarks);

public sealed record StockTransferReceiveResult(
    long TransferId,
    string ToLocationType,
    long ToLocationId,
    IReadOnlyList<long> VariantIds);
