namespace RetailPOS.Core.Entities;

public class Bill
{
    public long Id { get; set; }
    public long SupplierId { get; set; }
    public long? PoId { get; set; }
    public decimal AmountDue { get; set; }
    public DateTime DueDate { get; set; }
    public string Status { get; set; } = "unpaid";

    // Navigation properties
    public virtual Supplier Supplier { get; set; } = null!;
    public virtual PurchaseOrder? PurchaseOrder { get; set; }
}
