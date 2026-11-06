namespace RetailPOS.Core.Entities;

/// <summary>
/// A role the user may explicitly act as through context switching.
/// Grants nothing by itself: <see cref="User.ContextSwitchEnabled"/> must also be set.
/// </summary>
public class UserContextRole
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public long RoleId { get; set; }

    /// <summary>Denormalized for data isolation; a mapping is only honoured inside this business.</summary>
    public long BusinessId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual User? User { get; set; }
    public virtual Role? Role { get; set; }
    public virtual Business? Business { get; set; }
}
