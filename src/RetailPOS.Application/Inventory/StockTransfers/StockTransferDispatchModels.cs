namespace RetailPOS.Application.Inventory.StockTransfers;

public sealed record StockTransferDispatchResult(
    long TransferId,
    bool WasDispatched,
    string FromLocationType,
    long FromLocationId,
    IReadOnlyList<long> VariantIds);
