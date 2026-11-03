namespace RetailPOS.Core.Entities;

public class SupplierPaymentAllocation
{
    public long Id { get; set; }
    public long SupplierPaymentId { get; set; }
    public string PayableDocumentType { get; set; } = string.Empty;
    public long PayableDocumentId { get; set; }
    public long? SupplierPayableDocumentId { get; set; }
    public decimal AllocatedAmount { get; set; }
    public decimal OutstandingBeforeAllocation { get; set; }
    public decimal OutstandingAfterAllocation { get; set; }
    public DateTime AllocationDate { get; set; } = DateTime.UtcNow;
    public bool IsReversed { get; set; }
    public string Status { get; set; } = "pending";
    public DateTime? EffectiveDate { get; set; }
    public DateTime? ReversedEffectiveDate { get; set; }
    public long? ReversalJournalEntryId { get; set; }
    public long? JournalEntryId { get; set; }
    public long? ReversalOfAllocationId { get; set; }
    public string? SourceReference { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public long? CreatedBy { get; set; }

    public virtual SupplierPayment SupplierPayment { get; set; } = null!;
    public virtual SupplierPayableDocument? SupplierPayableDocument { get; set; }
}
