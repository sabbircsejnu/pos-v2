namespace RetailPOS.Core.Entities;

public static class SupplierPayableDocumentStatuses
{
    public const string Open = "open";
    public const string Partial = "partial";
    public const string Paid = "paid";
    public const string Disputed = "disputed";
    public const string Corrected = "corrected";
    public const string LegacyUnreconciled = "legacy_unreconciled";
}

public class SupplierPayableDocument
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long SupplierId { get; set; }
    public long? WarehouseId { get; set; }
    public string SourceDocumentType { get; set; } = string.Empty;
    public long SourceDocumentId { get; set; }
    public string SourceReference { get; set; } = string.Empty;
    public long SourceJournalEntryId { get; set; }
    public decimal OriginalPayableAmount { get; set; }
    public DateTime RecognitionDate { get; set; }
    public string? SupplierInvoiceNumber { get; set; }
    public DateTime? SupplierInvoiceDate { get; set; }
    public int PaymentTermsDays { get; set; }
    public DateTime DueDate { get; set; }
    public string Status { get; set; } = SupplierPayableDocumentStatuses.Open;
    public bool IsDisputed { get; set; }
    public string? DisputeReason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public long? CreatedBy { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public long? UpdatedBy { get; set; }

    public virtual Supplier Supplier { get; set; } = null!;
    public virtual Warehouse? Warehouse { get; set; }
    public virtual JournalEntry SourceJournalEntry { get; set; } = null!;
    public virtual ICollection<SupplierPaymentAllocation> PaymentAllocations { get; set; } = new List<SupplierPaymentAllocation>();
    public virtual ICollection<SupplierAdvanceUtilization> AdvanceUtilizations { get; set; } = new List<SupplierAdvanceUtilization>();
}

public class SupplierAdvanceUtilization
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long SupplierAdvanceId { get; set; }
    public long SupplierPayableDocumentId { get; set; }
    public decimal Amount { get; set; }
    public DateTime EffectiveDate { get; set; }
    public long JournalEntryId { get; set; }
    public string Status { get; set; } = "effective";
    public long? ReversalOfUtilizationId { get; set; }
    public string? Reason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public long? CreatedBy { get; set; }

    public virtual SupplierAdvance SupplierAdvance { get; set; } = null!;
    public virtual SupplierPayableDocument SupplierPayableDocument { get; set; } = null!;
    public virtual JournalEntry JournalEntry { get; set; } = null!;
}