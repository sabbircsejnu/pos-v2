using System.Security.Claims;
using RetailPOS.Application.Identity.Permissions;

namespace RetailPOS.API.Authorization;

/// <summary>
/// Extension helpers for ClaimsPrincipal permission checks.
/// The JWT encodes each granted permission as an individual "permission" claim.
/// All checks delegate to <see cref="PermissionCatalog.IsGranted"/> so that policies, controllers and
/// access adapters evaluate the wildcard "*" and permission aliases identically.
/// </summary>
public static class ClaimsPrincipalExtensions
{
    public const string PermissionClaimType = "permission";

    /// <summary>
    /// Returns true when the caller holds the specified permission, one of its aliases, or the wildcard "*".
    /// </summary>
    public static bool HasPermission(this ClaimsPrincipal? user, string permission)
        => user is not null
        && PermissionCatalog.IsGranted(
            user.FindAll(PermissionClaimType).Select(claim => claim.Value),
            permission);

    /// <summary>
    /// Returns true when the caller has the <c>products.view_cost</c> permission.
    /// Use this to decide whether cost/margin data should be included in API responses.
    /// </summary>
    public static bool CanViewCost(this ClaimsPrincipal user)
        => user.HasPermission("products.view_cost");

    /// <summary>
    /// True only when the caller holds exactly this permission claim. Unlike <see cref="HasPermission"/> it ignores
    /// the Business wildcard and aliases, for approval exceptions that must never be implied (D7).
    /// </summary>
    public static bool HasExplicitPermission(this ClaimsPrincipal? user, string permission)
        => user is not null
        && user.FindAll(PermissionClaimType).Any(claim =>
            string.Equals(claim.Value, permission, StringComparison.OrdinalIgnoreCase));
}
