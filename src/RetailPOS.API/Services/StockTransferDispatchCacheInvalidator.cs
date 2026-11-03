using RetailPOS.Application.Inventory.Abstractions;

namespace RetailPOS.API.Services;

public sealed class StockTransferDispatchCacheInvalidator(IPosCacheService posCache)
    : IStockTransferDispatchCacheInvalidator
{
    public Task InvalidateAsync(
        long variantId,
        long outletId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return posCache.InvalidateStockAsync(variantId, outletId);
    }
}
