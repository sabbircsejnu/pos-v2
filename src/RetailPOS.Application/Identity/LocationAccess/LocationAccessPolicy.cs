namespace RetailPOS.Application.Identity.LocationAccess;

/// <summary>
/// Operation-based location policies. Each operation states which user access setting decides the
/// locations it may use; callers must pick a policy explicitly instead of inheriting another module's rule.
/// Super Admin and Business Owner (wildcard / unset scope) keep business-wide access under every policy.
/// </summary>
public enum LocationAccessPolicy
{
    /// <summary>Stock visibility. Follows the user's Step 3 Inventory Location Access scope. Default for legacy callers.</summary>
    InventoryStockView = 0,

    /// <summary>Stock count create/list/detail. Only the user's Step 5 outlet/warehouse assignments.</summary>
    StockCountOperation = 1,

    /// <summary>Stock transfer source (create, update and source-side actions). Only the user's Step 5 assignments.</summary>
    StockTransferSource = 2,

    /// <summary>Stock transfer destination lookup. Every location of the current business, regardless of Step 3 or Step 5.</summary>
    StockTransferDestination = 3
}

public static class LocationAccessPolicies
{
    public static bool TryParse(string? value, out LocationAccessPolicy policy)
    {
        policy = LocationAccessPolicy.InventoryStockView;
        if (string.IsNullOrWhiteSpace(value)
            || !Enum.TryParse<LocationAccessPolicy>(value.Trim(), ignoreCase: true, out var parsed)
            || !Enum.IsDefined(parsed)
            || char.IsDigit(value.Trim()[0]))
        {
            return false;
        }

        policy = parsed;
        return true;
    }
}
