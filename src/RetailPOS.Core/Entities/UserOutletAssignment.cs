namespace RetailPOS.Core.Entities;

/// <summary>
/// Represents assignment of a user to one or more outlets for inventory operations.
/// </summary>
public class UserOutletAssignment
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public long OutletId { get; set; }
    public long BusinessId { get; set; }
    public bool IsPrimary { get; set; } = false;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public virtual User? User { get; set; }
    public virtual Outlet? Outlet { get; set; }
    public virtual Business? Business { get; set; }
}
