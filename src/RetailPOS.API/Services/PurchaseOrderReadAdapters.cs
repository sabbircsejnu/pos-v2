using RetailPOS.Application.PurchaseOrders.Abstractions;

namespace RetailPOS.API.Services;

public sealed class PurchaseOrderTenantScopeAdapter(ITenantAccessService tenantAccess)
    : IPurchaseOrderTenantScope
{
    public Task<long?> GetBusinessIdAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        long? businessId = tenantAccess.RequireBusinessId();
        return Task.FromResult(businessId);
    }
}

/// <summary>
/// Reuses the central outlet/warehouse write-access rules for purchase destinations.
/// </summary>
public sealed class PurchaseOrderLocationAuthorizerAdapter(IUserOutletAccessService outletAccess)
    : IPurchaseOrderLocationAuthorizer
{
    public async Task AuthorizeAsync(
        string locationType,
        long locationId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var location = await outletAccess.EnforceWriteLocationAsync(locationId, locationType);
        if (location.LocationId != locationId
            || !string.Equals(location.LocationType, locationType, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException($"You are not authorized to purchase into {locationType} #{locationId}.");
    }
}
