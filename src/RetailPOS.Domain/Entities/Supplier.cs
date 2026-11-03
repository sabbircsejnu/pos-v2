namespace RetailPOS.Core.Entities;

public class Supplier
{
    public long Id { get; set; }
    public long? BusinessId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Contact { get; set; }
    public string? Address { get; set; }
    public decimal CreditLimit { get; set; } = 0;
    public int? PaymentTermsDays { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual ICollection<PurchaseOrder> PurchaseOrders { get; set; } = new List<PurchaseOrder>();
    public virtual ICollection<Bill> Bills { get; set; } = new List<Bill>();
    public virtual Business? Business { get; set; }
}
