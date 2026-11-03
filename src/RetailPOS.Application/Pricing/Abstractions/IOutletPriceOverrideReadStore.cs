using RetailPOS.Application.Pricing.ReadModels;

namespace RetailPOS.Application.Pricing.Abstractions;

public interface IOutletPriceOverrideReadStore
{
    Task<IReadOnlyList<OutletPriceOverrideReadModel>> GetByOutletAsync(
        long outletId,
        CancellationToken cancellationToken);

    Task<OutletPriceOverrideReadModel?> GetByIdAsync(
        long id,
        CancellationToken cancellationToken);
}
