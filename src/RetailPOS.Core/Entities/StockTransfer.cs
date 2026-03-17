namespace RetailPOS.Core.Entities;

public class StockTransfer
{
    public long Id { get; set; }
    public long FromLocationId { get; set; }
    public string FromLocationType { get; set; } = string.Empty;
    public long ToLocationId { get; set; }
    public string ToLocationType { get; set; } = string.Empty;
    public DateTime TransferDate { get; set; }
    public string Status { get; set; } = "pending";
    public long? ApprovedBy { get; set; }
    public long? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual User? Approver { get; set; }
    public virtual User? Creator { get; set; }
    public virtual ICollection<StockTransferItem> Items { get; set; } = new List<StockTransferItem>();
}
