using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Abstractions;
using RetailPOS.Application.Pricing.Abstractions;
using RetailPOS.Application.Pricing.ReadModels;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Pricing;

public sealed class OutletPriceOverrideReadStore(RetailPOSDbContext dbContext, IBusinessScope businessScope)
    : IOutletPriceOverrideReadStore
{
    public async Task<IReadOnlyList<OutletPriceOverrideReadModel>> GetByOutletAsync(
        long outletId,
        CancellationToken cancellationToken)
    {
        var businessId = businessScope.RequireBusinessId();
        return await dbContext.OutletPriceOverrides
            .AsNoTracking()
            .Where(priceOverride => priceOverride.BusinessId == businessId && priceOverride.OutletId == outletId)
            .OrderBy(priceOverride => priceOverride.ProductVariant.Product.Name)
            .ThenBy(priceOverride => priceOverride.ProductVariant.Name)
            .Select(priceOverride => new OutletPriceOverrideReadModel(
                priceOverride.Id,
                priceOverride.OutletId,
                string.Empty,
                priceOverride.ProductVariantId,
                priceOverride.ProductVariant.Name,
                priceOverride.ProductVariant.Product.Name,
                priceOverride.OverrideType,
                priceOverride.OverrideValue,
                priceOverride.IsActive,
                priceOverride.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public Task<OutletPriceOverrideReadModel?> GetByIdAsync(
        long id,
        CancellationToken cancellationToken)
    {
        var businessId = businessScope.RequireBusinessId();
        return dbContext.OutletPriceOverrides
            .AsNoTracking()
            .Where(priceOverride => priceOverride.Id == id && priceOverride.BusinessId == businessId)
            .Select(priceOverride => new OutletPriceOverrideReadModel(
                priceOverride.Id,
                priceOverride.OutletId,
                priceOverride.Outlet.Name,
                priceOverride.ProductVariantId,
                priceOverride.ProductVariant.Name,
                priceOverride.ProductVariant.Product.Name,
                priceOverride.OverrideType,
                priceOverride.OverrideValue,
                priceOverride.IsActive,
                priceOverride.CreatedAt))
            .FirstOrDefaultAsync(cancellationToken);
    }
}
