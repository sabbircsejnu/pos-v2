using RetailPOS.API.Authorization;
using RetailPOS.Application.Abstractions;
using RetailPOS.Application.Identity.LocationAccess;
using RetailPOS.Application.Identity.Permissions;
using RetailPOS.Application.OwnerDashboards;
using RetailPOS.Application.Settings.Abstractions;

namespace RetailPOS.API.Services;

/// <summary>
/// The reporting scope of both dashboards: every location of the current Business (SRS §2.1). Step 3, Step 5, the default
/// outlet and the selected location do not limit reads. The Business never comes from the client, so nothing here can
/// cross a Business boundary. The existing transfer rules are read only to say whether the transfer form would accept a
/// source; the transfer API still enforces them.
/// </summary>
public sealed class OwnerDashboardScopeProvider(
    IBusinessScope businessScope,
    IUserOutletAccessService outletAccess,
    IHttpContextAccessor httpContextAccessor,
    ICompanyTimeZoneProvider timeZones,
    IInventoryDashboardReadStore locationStore) : IOwnerDashboardScopeProvider
{
    public async Task<OwnerDashboardScope> GetAsync(CancellationToken cancellationToken)
    {
        var businessId = businessScope.RequireBusinessId();
        var user = httpContextAccessor.HttpContext?.User
            ?? throw new UnauthorizedAccessException("The dashboard needs an authenticated user.");

        var locations = await locationStore.GetBusinessLocationsAsync(businessId, cancellationToken);
        var outletIds = locations.Outlets.Select(outlet => outlet.Id).ToHashSet();
        var transferSource = await outletAccess.GetAuthorizedOutletsAsync(LocationAccessPolicy.StockTransferSource);
        var defaults = await outletAccess.GetAuthorizedOutletsAsync();

        return new OwnerDashboardScope(
            businessId,
            await timeZones.GetTimeZoneIdAsync(businessId, cancellationToken),
            DateOnly.FromDateTime(await timeZones.GetTodayAsync(businessId, cancellationToken)),
            locations.Outlets,
            locations.Warehouses,
            transferSource.Outlets
                .Where(location => outletIds.Contains(location.Id))
                .Select(location => location.Id)
                .ToList(),
            defaults.DefaultLocationType == OwnerDashboardLocationTypes.Outlet ? defaults.DefaultLocationId : null,
            user.HasPermission("stock_transfers.create"),
            user.HasPermission("stock_transfers.transfer_from_any_location"));
    }
}
