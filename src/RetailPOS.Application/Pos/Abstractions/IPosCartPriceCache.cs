using RetailPOS.Application.Pos.ReadModels;

namespace RetailPOS.Application.Pos.Abstractions;

public interface IPosCartPriceCache
{
    /// <summary>
    /// <paramref name="scope"/> identifies the Business and tax policy the price was calculated under so a cached price is
    /// never served to another Business or after the tax policy changed.
    /// </summary>
    Task<PosCartPriceReadModel?> GetAsync(
        string scope,
        long variantId,
        long? outletId,
        int quantity,
        CancellationToken cancellationToken);

    Task SetAsync(
        string scope,
        long variantId,
        long? outletId,
        int quantity,
        PosCartPriceReadModel result,
        CancellationToken cancellationToken);
}
