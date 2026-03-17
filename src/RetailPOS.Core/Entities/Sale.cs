namespace RetailPOS.Core.Entities;

public class Sale
{
    public long Id { get; set; }
    public long OutletId { get; set; }
    public long? CustomerId { get; set; }
    public DateTime SaleDate { get; set; } = DateTime.UtcNow;
    public decimal TotalAmount { get; set; }
    public decimal Discount { get; set; } = 0;
    public decimal Tax { get; set; } = 0;
    public string PaymentMethod { get; set; } = string.Empty;
    public string Status { get; set; } = "completed";
    public long CashierId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual Outlet Outlet { get; set; } = null!;
    public virtual Customer? Customer { get; set; }
    public virtual User Cashier { get; set; } = null!;
    public virtual ICollection<SaleItem> Items { get; set; } = new List<SaleItem>();
}
