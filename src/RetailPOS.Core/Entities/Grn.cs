namespace RetailPOS.Core.Entities;

public class Grn
{
    public long Id { get; set; }
    public long PoId { get; set; }
    public DateTime ReceivedDate { get; set; }
    public string Status { get; set; } = "full"; // partial, full
    public long? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual PurchaseOrder PurchaseOrder { get; set; } = null!;
    public virtual User? Creator { get; set; }
    public virtual ICollection<GrnItem> Items { get; set; } = new List<GrnItem>();
}
