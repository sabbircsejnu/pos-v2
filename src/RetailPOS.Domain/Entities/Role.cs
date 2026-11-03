namespace RetailPOS.Core.Entities;

public class Role
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Permissions { get; set; } = "[]"; // JSON array as string
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual ICollection<User> Users { get; set; } = new List<User>();
}
