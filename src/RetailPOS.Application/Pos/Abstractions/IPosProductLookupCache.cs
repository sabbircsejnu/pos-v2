using RetailPOS.Application.Pos.ReadModels;

namespace RetailPOS.Application.Pos.Abstractions;

public interface IPosProductLookupCache
{
    Task<PosProductLookupReadModel?> GetLookupAsync(
        PosProductLookupMode mode,
        string query,
        long? outletId,
        CancellationToken cancellationToken);

    Task SetLookupAsync(
        PosProductLookupMode mode,
        string query,
        long? outletId,
        PosProductLookupReadModel result,
        CancellationToken cancellationToken);

    Task<int?> GetStockAsync(
        long variantId,
        long outletId,
        CancellationToken cancellationToken);

    Task SetStockAsync(
        long variantId,
        long outletId,
        int quantity,
        CancellationToken cancellationToken);
}
