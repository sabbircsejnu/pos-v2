using RetailPOS.Application.Authentication.Abstractions;
using RetailPOS.Application.Authentication.Commands;
using RetailPOS.Application.Identity.LocationAccess;
using RetailPOS.Application.Inventory.StockAdjustments;
using RetailPOS.Application.Inventory.StockCounts.Abstractions;

namespace RetailPOS.Application.Inventory.StockCounts;

public sealed class StockCountAdjustmentApproverPolicy(
    IAuthenticationProfileStore profiles,
    IUserOutletAccess outletAccess) : IStockCountAdjustmentApproverPolicy
{
    public const string ApprovePermission = "StockCount.Approve";
    public const string PostPermission = "stock_adjustments.approve";

    public static bool HasCombinedPostingAuthorization(IEnumerable<string> permissions)
    {
        var granted = permissions as ICollection<string> ?? permissions.ToArray();
        return granted.Contains("*", StringComparer.Ordinal)
            || (granted.Contains(ApprovePermission, StringComparer.Ordinal)
                && granted.Contains(PostPermission, StringComparer.Ordinal));
    }

    public async Task<StockCountAdjustmentApproverAuthorization?> AuthorizeAsync(
        long approverUserId,
        long businessId,
        long locationId,
        string locationType,
        CancellationToken cancellationToken)
    {
        var profile = await profiles.FindProfileAsync(approverUserId, cancellationToken);
        if (profile is null
            || !profile.IsActive
            || profile.MustResetPassword
            || profile.BusinessId != businessId)
        {
            return null;
        }

        var permissions = LoginCommandHandler.BuildEffectivePermissions(
            profile.RolePermissions,
            profile.PermissionOverrides);
        if (!HasCombinedPostingAuthorization(permissions))
            return null;

        try
        {
            var authorized = await outletAccess.GetAuthorizedOutletsAsync(new UserOutletAccessContext(
                profile.Id,
                IsSuperAdmin: false,
                IsBusinessOwner: string.Equals(profile.RoleName, "BusinessOwner", StringComparison.OrdinalIgnoreCase),
                EffectiveBusinessId: businessId,
                EffectiveOutletId: null));
            var locations = authorized.Outlets
                .Select(location => new StockAdjustmentLocationScope(location.Id, "outlet"))
                .Concat(authorized.Warehouses
                    .Select(location => new StockAdjustmentLocationScope(location.Id, "warehouse")))
                .ToArray();
            return locations.Any(location =>
                location.Id == locationId
                && location.Type.Equals(locationType, StringComparison.OrdinalIgnoreCase))
                ? new StockCountAdjustmentApproverAuthorization(locations)
                : null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
