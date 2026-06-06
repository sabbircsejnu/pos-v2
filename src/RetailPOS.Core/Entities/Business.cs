namespace RetailPOS.Core.Entities;

public class Business
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public virtual ICollection<User> Users { get; set; } = new List<User>();
    public virtual ICollection<Outlet> Outlets { get; set; } = new List<Outlet>();
    public virtual ICollection<Warehouse> Warehouses { get; set; } = new List<Warehouse>();
    public virtual ICollection<Account> Accounts { get; set; } = new List<Account>();
}