using System.Security.Claims;

namespace RetailPOS.API.Authorization;

/// <summary>
/// Extension helpers for ClaimsPrincipal permission checks.
/// The JWT encodes each granted permission as an individual "permission" claim.
/// The wildcard value "*" grants all permissions.
/// </summary>
public static class ClaimsPrincipalExtensions
{
    private const string PermissionClaimType = "permission";

    /// <summary>
    /// Returns true when the caller holds the specified permission OR the wildcard "*".
    /// </summary>
    public static bool HasPermission(this ClaimsPrincipal user, string permission)
        => user.HasClaim(PermissionClaimType, "*")
        || user.HasClaim(PermissionClaimType, permission);

    /// <summary>
    /// Returns true when the caller has the <c>products.view_cost</c> permission.
    /// Use this to decide whether cost/margin data should be included in API responses.
    /// </summary>
    public static bool CanViewCost(this ClaimsPrincipal user)
        => user.HasPermission("products.view_cost");
}
