using Microsoft.AspNetCore.Authorization;

namespace RetailPOS.API.Authorization;

/// <summary>
/// Named any-of policies for read-only lookups that several screens embed (for example the variant
/// picker). A caller needs only one of the listed permissions, each of which is held by a screen that
/// genuinely uses the lookup, so a screen's own view/create permission is never blocked by an
/// unrelated management permission. Data scope (Business and Outlet/Warehouse) is enforced by the
/// endpoint itself and is not widened here.
/// </summary>
public static class LookupAuthorizationPolicies
{
    public const string ProductVariantSearch = "lookup.product_variants";

    private static readonly IReadOnlyDictionary<string, string[]> Definitions =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [ProductVariantSearch] =
            [
                "products.view",
                "inventory.view",
                "stock_adjustments.create", "stock_adjustments.edit",
                "stock_transfers.view", "stock_transfers.create",
                "purchases.create", "purchases.edit",
                "reports.inventory"
            ]
        };

    public static IReadOnlyCollection<string> PolicyNames => Definitions.Keys.ToArray();

    public static IReadOnlyList<string> PermissionsFor(string policyName) => Definitions[policyName];

    public static void Register(AuthorizationOptions options)
    {
        foreach (var (name, permissions) in Definitions)
        {
            options.AddPolicy(name, policy => policy.RequireAssertion(
                context => permissions.Any(context.User.HasPermission)));
        }
    }
}
