namespace RetailPOS.Core.Entities;

public class SupplierPayment
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long? OutletId { get; set; }
    public long SupplierId { get; set; }
    public DateTime PaymentDate { get; set; }
    public DateTime? PostingDate { get; set; }
    public DateTime? BusinessDate { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public long PaymentAccountId { get; set; }
    public decimal Amount { get; set; }
    public decimal AllocatedAmount { get; set; }
    public decimal AdvanceAmount { get; set; }
    public string Status { get; set; } = "draft";
    public string? ReferenceNumber { get; set; }
    public string? Notes { get; set; }
    public string? IdempotencyKey { get; set; }
    public long? JournalEntryId { get; set; }
    public long? ReversalOfPaymentId { get; set; }
    public long? ReversalJournalEntryId { get; set; }
    public DateTime? ReversedAt { get; set; }
    public long? ReversedBy { get; set; }
    public string? ReversalReason { get; set; }
    public long? PostedBy { get; set; }
    public DateTime? PostedAt { get; set; }
    public long? SubmittedBy { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public long? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public long? CreatedBy { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public long? UpdatedBy { get; set; }

    public virtual Supplier Supplier { get; set; } = null!;
    public virtual ICollection<SupplierPaymentAllocation> Allocations { get; set; } = new List<SupplierPaymentAllocation>();
    public virtual ICollection<SupplierAdvance> Advances { get; set; } = new List<SupplierAdvance>();
}
