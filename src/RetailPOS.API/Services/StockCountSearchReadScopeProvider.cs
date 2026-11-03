using RetailPOS.API.Authorization;
using RetailPOS.Application.Identity.LocationAccess;
using RetailPOS.Application.Inventory.StockCounts;
using RetailPOS.Application.Inventory.StockCounts.Abstractions;

namespace RetailPOS.API.Services;

public sealed class StockCountSearchReadScopeProvider(
    ITenantAccessService tenantAccess,
    IUserOutletAccessService locationAccess)
    : IStockCountSearchReadScopeProvider
{
    private const LocationAccessPolicy Policy = LocationAccessPolicy.StockCountOperation;

    public async Task<StockCountReadScope> GetCreateScopeAsync(
        long? locationId,
        string? locationType,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var resolved = await locationAccess.ResolveAndAuthorizeLocationAsync(Policy, locationId, locationType);
        if (!resolved.LocationId.HasValue || string.IsNullOrWhiteSpace(resolved.LocationType))
        {
            throw new InvalidOperationException("A valid authorized location is required.");
        }

        await locationAccess.EnforceWriteLocationAsync(Policy, resolved.LocationId.Value, resolved.LocationType);
        return new StockCountReadScope(
            tenantAccess.RequireBusinessId(),
            resolved.LocationId,
            resolved.LocationType,
            resolved.LocationType == "outlet" ? [resolved.LocationId.Value] : [],
            resolved.LocationType == "warehouse" ? [resolved.LocationId.Value] : []);
    }

    public async Task<StockCountReadScope> GetAsync(
        long? locationId,
        string? locationType,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var businessId = tenantAccess.RequireBusinessId();
        var authorized = await locationAccess.GetAuthorizedOutletsAsync(Policy);
        var resolved = locationId.HasValue || !string.IsNullOrWhiteSpace(locationType)
            ? await locationAccess.ResolveAndAuthorizeLocationAsync(Policy, locationId, locationType)
            : (LocationId: (long?)null, LocationType: (string?)null);
        return new StockCountReadScope(
            businessId,
            resolved.LocationId,
            resolved.LocationType,
            authorized.Outlets.Select(location => location.Id).ToArray(),
            authorized.Warehouses.Select(location => location.Id).ToArray());
    }

    public async Task EnsureDetailAccessAsync(
        long businessId,
        long locationId,
        string locationType,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        tenantAccess.EnsureBusinessMatch(businessId, "stock count");

        await locationAccess.ResolveAndAuthorizeLocationAsync(Policy, locationId, locationType);
    }
}
