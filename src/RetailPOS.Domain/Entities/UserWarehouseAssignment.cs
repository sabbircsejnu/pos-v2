namespace RetailPOS.Core.Entities;

/// <summary>
/// Represents the assignment of a user (typically WarehouseManager) to a warehouse.
/// Allows one user to manage multiple warehouses.
/// Ensures warehouse assignments stay within the business boundary.
/// </summary>
public class UserWarehouseAssignment
{
    public long Id { get; set; }

    /// <summary>
    /// The user being assigned to warehouse(s)
    /// </summary>
    public long UserId { get; set; }

    /// <summary>
    /// The warehouse to which the user is assigned
    /// </summary>
    public long WarehouseId { get; set; }

    /// <summary>
    /// The business context for this assignment (denormalized for data isolation)
    /// </summary>
    public long BusinessId { get; set; }

    /// <summary>
    /// Whether this is the primary/default warehouse for the user
    /// </summary>
    public bool IsPrimary { get; set; } = false;

    /// <summary>
    /// Whether this assignment is active
    /// </summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual User? User { get; set; }
    public virtual Warehouse? Warehouse { get; set; }
    public virtual Business? Business { get; set; }
}
