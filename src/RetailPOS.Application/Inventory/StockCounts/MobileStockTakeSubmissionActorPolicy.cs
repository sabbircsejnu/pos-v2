using RetailPOS.Application.Authentication.Abstractions;
using RetailPOS.Application.Authentication.Commands;
using RetailPOS.Application.Identity.LocationAccess;
using RetailPOS.Application.Inventory.StockCounts.Abstractions;

namespace RetailPOS.Application.Inventory.StockCounts;

/// <summary>
/// Rechecks the persisted initiating actor with the same role/permission/Outlet rules as the
/// Mobile submission endpoint, using current database state instead of the original token.
/// </summary>
public sealed class MobileStockTakeSubmissionActorPolicy(
    IAuthenticationProfileStore profiles,
    IUserOutletAccess outletAccess) : IMobileStockTakeSubmissionActorPolicy
{
    public const string SubmitPermission = "StockCount.Submit";

    public async Task<bool> IsStillAuthorizedAsync(
        MobileStockTakeSubmissionActorContext actor,
        CancellationToken cancellationToken)
    {
        var profile = await profiles.FindProfileAsync(actor.ActorUserId, cancellationToken);
        if (profile is null
            || !profile.IsActive
            || profile.MustResetPassword
            || profile.BusinessId != actor.BusinessId
            || !IsSupportedMobileStockTakeRole(profile.RoleName))
        {
            return false;
        }

        var permissions = LoginCommandHandler.BuildEffectivePermissions(
            profile.RolePermissions,
            profile.PermissionOverrides);
        if (!permissions.Any(permission =>
                string.Equals(permission, "*", StringComparison.Ordinal)
                || string.Equals(permission, SubmitPermission, StringComparison.Ordinal)))
        {
            return false;
        }

        try
        {
            var authorized = await outletAccess.GetAuthorizedOutletsAsync(new UserOutletAccessContext(
                profile.Id,
                IsSuperAdmin: false,
                IsBusinessOwner: string.Equals(profile.RoleName, "BusinessOwner", StringComparison.OrdinalIgnoreCase),
                EffectiveBusinessId: actor.BusinessId,
                EffectiveOutletId: null));
            return authorized.Outlets.Any(outlet => outlet.Id == actor.OutletId);
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static bool IsSupportedMobileStockTakeRole(string? role) =>
        string.Equals(role, "BusinessOwner", StringComparison.OrdinalIgnoreCase)
        || string.Equals(role, "SalesManager", StringComparison.OrdinalIgnoreCase)
        || string.Equals(role, "OutletManager", StringComparison.OrdinalIgnoreCase)
        || string.Equals(role, "Manager", StringComparison.OrdinalIgnoreCase);
}
