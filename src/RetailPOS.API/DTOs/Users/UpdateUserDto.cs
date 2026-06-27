using System.ComponentModel.DataAnnotations;

namespace RetailPOS.API.DTOs.Users;

public class UpdateUserDto
{
    public const string ScopeAssignedOnly = "assigned_only";
    public const string ScopeSpecificLocations = "specific_locations";
    public const string ScopeAllLocations = "all_locations";

    [Required(ErrorMessage = "Name is required")]
    [StringLength(100, ErrorMessage = "Name cannot exceed 100 characters")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Invalid email format")]
    [StringLength(100, ErrorMessage = "Email cannot exceed 100 characters")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Role ID is required")]
    public long RoleId { get; set; }

    /// <summary>
    /// Outlet assignment - optional for some roles, required for others
    /// </summary>
    public long? OutletId { get; set; }

    /// <summary>
    /// Warehouse assignments - only applicable for WarehouseManager and higher roles.
    /// WarehouseManager must have at least one warehouse assigned.
    /// </summary>
    public IEnumerable<long>? WarehouseIds { get; set; }

    /// <summary>
    /// Optional outlet assignments used when InventoryLocationAccessScope is specific_locations.
    /// </summary>
    public IEnumerable<long>? OutletIds { get; set; }

    /// <summary>
    /// Default working inventory location type (outlet | warehouse).
    /// </summary>
    [StringLength(20)]
    public string? DefaultLocationType { get; set; }

    /// <summary>
    /// Default working inventory location ID.
    /// </summary>
    public long? DefaultLocationId { get; set; }

    /// <summary>
    /// Controls inventory operation location scope for the user.
    /// allowed values: assigned_only | specific_locations | all_locations
    /// </summary>
    [Required]
    [StringLength(40)]
    public string InventoryLocationAccessScope { get; set; } = ScopeAssignedOnly;

    /// <summary>
    /// Optional explicit business assignment for SuperAdmin updates.
    /// If omitted, existing user's BusinessId is preserved.
    /// </summary>
    public long? BusinessId { get; set; }

    public bool IsActive { get; set; }
}
