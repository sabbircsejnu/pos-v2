namespace RetailPOS.Core.Entities;

public class SupplierAdvance
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
    public decimal RemainingAmount { get; set; }
    public decimal AppliedAmount { get; set; }
    public string Status { get; set; } = "draft";
    public string? ReferenceNumber { get; set; }
    public string? Notes { get; set; }
    public string? IdempotencyKey { get; set; }
    public long? SourcePaymentId { get; set; }
    public long? JournalEntryId { get; set; }
    public long? RelatedPaymentId { get; set; }
    public long? ReversalOfAdvanceId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public long? CreatedBy { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public long? UpdatedBy { get; set; }
    public long? SubmittedBy { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public long? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public long? PostedBy { get; set; }
    public DateTime? PostedAt { get; set; }

    public virtual Supplier Supplier { get; set; } = null!;
    public virtual SupplierPayment? SourcePayment { get; set; }
}
