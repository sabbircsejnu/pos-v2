using RetailPOS.Core.Entities;

namespace RetailPOS.Infrastructure.Repositories;

public interface IOutletPriceOverrideRepository
{
    /// <summary>Gets an override with Outlet and ProductVariant.Product navigation properties loaded.</summary>
    Task<OutletPriceOverride?> GetByIdAsync(long id);

    /// <summary>Returns the single active override for this outlet+variant combination, or null.</summary>
    Task<OutletPriceOverride?> GetActiveForVariantAsync(long outletId, long variantId);

    Task<IEnumerable<OutletPriceOverride>> GetByOutletAsync(long outletId);
    Task<IEnumerable<OutletPriceOverride>> GetByVariantAsync(long variantId);

    Task<OutletPriceOverride> CreateAsync(OutletPriceOverride entity);
    Task<OutletPriceOverride> UpdateAsync(OutletPriceOverride entity);
    Task<bool> DeleteAsync(long id);

    /// <summary>Returns true if any override already exists for the given outlet+variant (excluding excludeId).</summary>
    Task<bool> ExistsAsync(long outletId, long variantId, long? excludeId = null);
}
