namespace RetailPOS.Core.Entities;

public class Customer
{
    public long Id { get; set; }
    public string? Name { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public int LoyaltyPoints { get; set; } = 0;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual ICollection<Sale> Sales { get; set; } = new List<Sale>();
}
