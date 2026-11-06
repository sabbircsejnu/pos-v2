using RetailPOS.Application.Identity.LocationAccess;
using RetailPOS.Application.Sales.Abstractions;

namespace RetailPOS.API.Services;

public sealed class SaleReadScopeProvider(
    ITenantAccessService tenantAccess,
    IUserOutletAccessService outletAccess,
    IRoleSwitchContext roleSwitch) : ISaleReadScopeProvider
{
    public async Task<SaleReadScope> GetAsync(long? requestedOutletId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var businessId = tenantAccess.RequireBusinessId();

        // A Business Owner reads every outlet of their Business, whatever their inventory scope; acting in a switched
        // role, they follow the same rule as anyone else. Everyone else follows the sales outlet policy.
        var authorized = roleSwitch.IsBusinessOwner && !roleSwitch.IsRoleSwitched
            ? await outletAccess.GetAuthorizedOutletsAsync(LocationAccessPolicy.StockTransferDestination)
            : await outletAccess.GetAuthorizedOutletsAsync();
        var outletIds = authorized.Outlets.Select(outlet => outlet.Id).ToList();

        if (requestedOutletId is long outletId)
        {
            return outletIds.Contains(outletId)
                ? new SaleReadScope(businessId, [outletId])
                : throw new UnauthorizedAccessException("You are not authorized to view sales for this outlet.");
        }

        return outletIds.Count > 0
            ? new SaleReadScope(businessId, outletIds)
            : throw new UnauthorizedAccessException("Your account is not assigned to an authorized outlet.");
    }
}
