using RetailPOS.Application.Pricing.ReadModels;

namespace RetailPOS.Application.Pricing.Abstractions;

public interface IOutletPriceOverrideCommandStore
{
    Task<bool> OutletExistsAsync(long outletId, CancellationToken cancellationToken);
    Task<bool> ProductVariantExistsAsync(long productVariantId, CancellationToken cancellationToken);
    Task<bool> ExistsAsync(long outletId, long productVariantId, CancellationToken cancellationToken);
    Task<OutletPriceOverrideReadModel?> GetByIdAsync(long id, CancellationToken cancellationToken);

    Task<OutletPriceOverrideReadModel?> CreateAsync(
        OutletPriceOverrideWriteData priceOverride,
        CancellationToken cancellationToken);

    Task<OutletPriceOverrideReadModel?> UpdateAsync(
        long id,
        OutletPriceOverrideUpdateData priceOverride,
        CancellationToken cancellationToken);

    Task<bool> DeleteAsync(long id, CancellationToken cancellationToken);
}
