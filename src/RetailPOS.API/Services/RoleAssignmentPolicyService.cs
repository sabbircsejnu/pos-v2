namespace RetailPOS.API.Services;

/// <summary>
/// Implements role-based user assignment policy validation.
/// Centralizes outlet and warehouse assignment rules.
/// </summary>
public class RoleAssignmentPolicyService : IRoleAssignmentPolicyService
{
    // Roles that can operate without a specific outlet assignment
    private static readonly HashSet<string> OutletOptionalRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        RoleSwitchClaims.SuperAdminRoleName,      // Super Admin
        RoleSwitchClaims.BusinessOwnerRoleName,   // BusinessOwner
        "BusinessAdmin",                           // BusinessAdmin
        "AccountsAdmin",                           // AccountsAdmin
        "WarehouseManager"                         // WarehouseManager - outlet not required, warehouse required instead
    };

    // Roles that can have warehouse assignments
    private static readonly HashSet<string> WarehouseAssignableRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "WarehouseManager",                        // WarehouseManager - must have warehouse(s)
        RoleSwitchClaims.BusinessOwnerRoleName,   // BusinessOwner - can access all
        "BusinessAdmin"                            // BusinessAdmin - can access all
    };

    public string? ValidateOutletAssignment(string? roleName, long? outletId, IEnumerable<long>? warehouseIds = null)
    {
        if (string.IsNullOrWhiteSpace(roleName))
        {
            return "Role name is required";
        }

        bool isOutletRequired = IsOutletRequired(roleName);

        if (isOutletRequired && !outletId.HasValue)
        {
            return $"OutletId is required for {roleName}. Assign a default outlet.";
        }

        return null; // Valid
    }

    public string? ValidateWarehouseAssignment(string? roleName, IEnumerable<long>? warehouseIds)
    {
        if (string.IsNullOrWhiteSpace(roleName))
        {
            return "Role name is required";
        }

        if (!WarehouseAssignableRoles.Contains(roleName))
        {
            // Role does not support warehouse assignments; this is OK
            return null;
        }

        var warehouseList = warehouseIds?.ToList() ?? new List<long>();

        // WarehouseManager must have at least one warehouse
        if (string.Equals(roleName, "WarehouseManager", StringComparison.OrdinalIgnoreCase))
        {
            if (warehouseList.Count == 0)
            {
                return "WarehouseManager must be assigned to at least one warehouse.";
            }
        }

        return null; // Valid
    }

    public bool IsOutletRequired(string? roleName)
    {
        if (string.IsNullOrWhiteSpace(roleName))
            return false;

        // Outlet is required if role is NOT in the outlet-optional list
        return !OutletOptionalRoles.Contains(roleName);
    }

    public bool IsWarehouseRequired(string? roleName)
    {
        if (string.IsNullOrWhiteSpace(roleName))
            return false;

        // Warehouse is required only for WarehouseManager
        return string.Equals(roleName, "WarehouseManager", StringComparison.OrdinalIgnoreCase);
    }

    public IEnumerable<string> GetWarehouseAssignableRoles()
    {
        return WarehouseAssignableRoles;
    }

    public IEnumerable<string> GetOutletOptionalRoles()
    {
        return OutletOptionalRoles;
    }
}
