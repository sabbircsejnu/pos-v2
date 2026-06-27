namespace RetailPOS.API.Services;

/// <summary>
/// Centralized service for enforcing role-based user assignment policies.
/// Validates outlet and warehouse assignment requirements based on role type.
/// </summary>
public interface IRoleAssignmentPolicyService
{
    /// <summary>
    /// Validates outlet assignment according to role policy.
    /// </summary>
    /// <param name="roleName">Name of the role to validate against</param>
    /// <param name="outletId">Outlet ID being assigned (null for no assignment)</param>
    /// <param name="warehouseIds">List of warehouse IDs being assigned</param>
    /// <returns>Validation error message, or null if valid</returns>
    string? ValidateOutletAssignment(string? roleName, long? outletId, IEnumerable<long>? warehouseIds = null);

    /// <summary>
    /// Validates warehouse assignment according to role policy.
    /// </summary>
    /// <param name="roleName">Name of the role to validate against</param>
    /// <param name="warehouseIds">List of warehouse IDs being assigned</param>
    /// <returns>Validation error message, or null if valid</returns>
    string? ValidateWarehouseAssignment(string? roleName, IEnumerable<long>? warehouseIds);

    /// <summary>
    /// Determines if a role requires outlet assignment.
    /// </summary>
    bool IsOutletRequired(string? roleName);

    /// <summary>
    /// Determines if a role requires warehouse assignment.
    /// </summary>
    bool IsWarehouseRequired(string? roleName);

    /// <summary>
    /// Gets list of roles that can have warehouse assignments.
    /// </summary>
    IEnumerable<string> GetWarehouseAssignableRoles();

    /// <summary>
    /// Gets list of roles for which outlet is optional.
    /// </summary>
    IEnumerable<string> GetOutletOptionalRoles();
}
