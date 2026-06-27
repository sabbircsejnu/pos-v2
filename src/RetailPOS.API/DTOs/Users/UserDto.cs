namespace RetailPOS.API.DTOs.Users;

public class UserDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public long? RoleId { get; set; }
    public string? RoleName { get; set; }
    public long? OutletId { get; set; }
    public string? OutletName { get; set; }
    public long? BusinessId { get; set; }
    public string? BusinessName { get; set; }
    public string InventoryLocationAccessScope { get; set; } = CreateUserDto.ScopeAssignedOnly;
    public string? DefaultLocationType { get; set; }
    public long? DefaultLocationId { get; set; }

    /// <summary>
    /// Outlet assignments used when inventory access scope is specific locations.
    /// </summary>
    public IEnumerable<UserOutletAssignmentDto>? OutletAssignments { get; set; }
    
    /// <summary>
    /// Warehouse assignments for WarehouseManager and higher roles
    /// </summary>
    public IEnumerable<UserWarehouseAssignmentDto>? WarehouseAssignments { get; set; }
    
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// DTO representing a user's warehouse assignment
/// </summary>
public class UserWarehouseAssignmentDto
{
    public long Id { get; set; }
    public long WarehouseId { get; set; }
    public string? WarehouseName { get; set; }
    public bool IsPrimary { get; set; }
    public bool IsActive { get; set; }
}

public class UserOutletAssignmentDto
{
    public long Id { get; set; }
    public long OutletId { get; set; }
    public string? OutletName { get; set; }
    public bool IsPrimary { get; set; }
    public bool IsActive { get; set; }
}

