using RetailPOS.Application.Inventory.StockTransfers;

namespace RetailPOS.Application.Inventory.Abstractions;

public interface IStockTransferDispatchStore
{
    Task<StockTransferDispatchResult> DispatchApprovedAsync(
        long transferId,
        long businessId,
        long? userId,
        CancellationToken cancellationToken);

    Task<StockTransferDispatchResult> SubmitAndDispatchAsync(
        long transferId,
        long businessId,
        long? userId,
        bool autoApprove,
        CancellationToken cancellationToken);
}

public interface IStockTransferDispatchCacheInvalidator
{
    Task InvalidateAsync(
        long variantId,
        long outletId,
        CancellationToken cancellationToken);
}
