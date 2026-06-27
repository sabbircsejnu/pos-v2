namespace RetailPOS.Core.Entities;

public class PurchaseOrder
{
    public long Id { get; set; }
    /// <summary>Human-readable reference, e.g. PO-20260607-0001. Auto-generated on creation.</summary>
    public string PoNumber { get; set; } = string.Empty;
    public long SupplierId { get; set; }
    public long WarehouseId { get; set; }
    public DateTime OrderDate { get; set; }
    public DateTime? ExpectedDelivery { get; set; }
    /// <summary>Client-supplied idempotency key (UUID) for immediate purchase-receive de-duplication.</summary>
    public string? IdempotencyKey { get; set; }
    public decimal TotalAmount { get; set; }
    /// <summary>draft | pending | sent_back | approved | partially_received | fully_received | completed | cancelled | rejected</summary>
    public string Status { get; set; } = "draft";
    /// <summary>Optional notes for this purchase order.</summary>
    public string? Notes { get; set; }
    /// <summary>Reason provided when the PO is rejected or sent back for correction.</summary>
    public string? RejectionReason { get; set; }
    public long? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual Supplier Supplier { get; set; } = null!;
    public virtual Warehouse Warehouse { get; set; } = null!;
    public virtual User? Creator { get; set; }
    public virtual ICollection<PurchaseOrderItem> Items { get; set; } = new List<PurchaseOrderItem>();
    public virtual ICollection<Grn> Grns { get; set; } = new List<Grn>();
    public virtual ICollection<Bill> Bills { get; set; } = new List<Bill>();
}
