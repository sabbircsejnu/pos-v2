using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Inventory.StockAdjustments;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.ReadStores;

internal static class StockAdjustmentAccessGuard
{
    public static async Task EnsureAuthorizedLocationAsync(
        RetailPOSDbContext dbContext,
        long locationId,
        string locationType,
        StockAdjustmentCreateContext context,
        string operation,
        CancellationToken cancellationToken)
    {
        var normalizedType = locationType.ToLowerInvariant();
        var authorized = context.ReadScope.Locations.Any(location =>
            location.Id == locationId
            && location.Type.Equals(normalizedType, StringComparison.OrdinalIgnoreCase));
        var belongsToBusiness = normalizedType switch
        {
            "outlet" => await dbContext.Outlets.AnyAsync(
                outlet => outlet.Id == locationId
                    && outlet.BusinessId == context.ReadScope.BusinessId,
                cancellationToken),
            "warehouse" => await dbContext.Warehouses.AnyAsync(
                warehouse => warehouse.Id == locationId
                    && warehouse.BusinessId == context.ReadScope.BusinessId,
                cancellationToken),
            _ => false
        };
        if (!authorized || !belongsToBusiness)
        {
            throw new UnauthorizedAccessException(
                $"You are not authorized to {operation} adjustments for location #{locationId}.");
        }
    }
}
