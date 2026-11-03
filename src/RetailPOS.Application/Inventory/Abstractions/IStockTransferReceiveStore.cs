using RetailPOS.Application.Inventory.StockTransfers;

namespace RetailPOS.Application.Inventory.Abstractions;

public interface IStockTransferReceiveStore
{
    Task<StockTransferReceiveResult> ReceiveAsync(
        long transferId,
        long businessId,
        StockTransferReceiveInput? input,
        long? receivedBy,
        CancellationToken cancellationToken);
}

public interface IStockTransferReceiveCacheInvalidator
{
    Task InvalidateAsync(
        long variantId,
        long outletId,
        CancellationToken cancellationToken);
}
