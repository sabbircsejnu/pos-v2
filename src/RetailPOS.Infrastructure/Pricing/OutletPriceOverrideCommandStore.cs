using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Abstractions;
using RetailPOS.Application.Pricing.Abstractions;
using RetailPOS.Application.Pricing.ReadModels;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Pricing;

public sealed class OutletPriceOverrideCommandStore(RetailPOSDbContext dbContext, IBusinessScope businessScope)
    : IOutletPriceOverrideCommandStore
{
    public Task<bool> OutletExistsAsync(long outletId, CancellationToken cancellationToken)
    {
        var businessId = businessScope.RequireBusinessId();
        return dbContext.Outlets.AnyAsync(
            outlet => outlet.Id == outletId && outlet.BusinessId == businessId,
            cancellationToken);
    }

    public Task<bool> ProductVariantExistsAsync(
        long productVariantId,
        CancellationToken cancellationToken)
    {
        var businessId = businessScope.RequireBusinessId();
        return dbContext.ProductVariants.AnyAsync(
            variant => variant.Id == productVariantId && variant.BusinessId == businessId,
            cancellationToken);
    }

    public Task<bool> ExistsAsync(
        long outletId,
        long productVariantId,
        CancellationToken cancellationToken)
    {
        var businessId = businessScope.RequireBusinessId();
        return dbContext.OutletPriceOverrides.AnyAsync(
            priceOverride =>
                priceOverride.BusinessId == businessId &&
                priceOverride.OutletId == outletId &&
                priceOverride.ProductVariantId == productVariantId &&
                priceOverride.IsActive,
            cancellationToken);
    }

    public Task<OutletPriceOverrideReadModel?> GetByIdAsync(
        long id,
        CancellationToken cancellationToken) =>
        QueryReadModels()
            .Where(priceOverride => priceOverride.Id == id)
            .Select(priceOverride => ToReadModel(priceOverride))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<OutletPriceOverrideReadModel?> CreateAsync(
        OutletPriceOverrideWriteData data,
        CancellationToken cancellationToken)
    {
        var priceOverride = new OutletPriceOverride
        {
            BusinessId = businessScope.RequireBusinessId(),
            OutletId = data.OutletId,
            ProductVariantId = data.ProductVariantId,
            OverrideType = data.OverrideType,
            OverrideValue = data.OverrideValue,
            IsActive = data.IsActive
        };

        dbContext.OutletPriceOverrides.Add(priceOverride);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await GetByIdAsync(priceOverride.Id, cancellationToken);
    }

    public async Task<OutletPriceOverrideReadModel?> UpdateAsync(
        long id,
        OutletPriceOverrideUpdateData data,
        CancellationToken cancellationToken)
    {
        var businessId = businessScope.RequireBusinessId();
        var priceOverride = await dbContext.OutletPriceOverrides.FirstOrDefaultAsync(
            candidate => candidate.Id == id && candidate.BusinessId == businessId,
            cancellationToken);
        if (priceOverride is null)
        {
            return null;
        }

        priceOverride.OverrideType = data.OverrideType;
        priceOverride.OverrideValue = data.OverrideValue;
        priceOverride.IsActive = data.IsActive;
        priceOverride.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(id, cancellationToken);
    }

    public async Task<bool> DeleteAsync(long id, CancellationToken cancellationToken)
    {
        var businessId = businessScope.RequireBusinessId();
        var priceOverride = await dbContext.OutletPriceOverrides.FirstOrDefaultAsync(
            candidate => candidate.Id == id && candidate.BusinessId == businessId,
            cancellationToken);
        if (priceOverride is null)
        {
            return false;
        }

        dbContext.OutletPriceOverrides.Remove(priceOverride);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private IQueryable<OutletPriceOverride> QueryReadModels()
    {
        var businessId = businessScope.RequireBusinessId();
        return dbContext.OutletPriceOverrides
            .AsNoTracking()
            .Where(priceOverride => priceOverride.BusinessId == businessId)
            .Include(priceOverride => priceOverride.Outlet)
            .Include(priceOverride => priceOverride.ProductVariant)
            .ThenInclude(variant => variant.Product);
    }

    private static OutletPriceOverrideReadModel ToReadModel(OutletPriceOverride priceOverride) =>
        new(
            priceOverride.Id,
            priceOverride.OutletId,
            priceOverride.Outlet?.Name ?? string.Empty,
            priceOverride.ProductVariantId,
            priceOverride.ProductVariant?.Name ?? string.Empty,
            priceOverride.ProductVariant?.Product?.Name ?? string.Empty,
            priceOverride.OverrideType,
            priceOverride.OverrideValue,
            priceOverride.IsActive,
            priceOverride.CreatedAt);
}
