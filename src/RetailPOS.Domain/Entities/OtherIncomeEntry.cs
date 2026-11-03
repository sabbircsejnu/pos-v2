namespace RetailPOS.Core.Entities;

public static class OtherIncomeStatuses
{
    public const string Draft = "Draft";
    public const string Submitted = "Submitted";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
    public const string Cancelled = "Cancelled";
}

public class OtherIncomeEntry
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long? OutletId { get; set; }
    public DateTime IncomeDate { get; set; }
    public string IncomeCategory { get; set; } = string.Empty;
    public long? IncomeAccountId { get; set; }
    public decimal Amount { get; set; }
    public decimal? TaxableAmount { get; set; }
    public decimal? VatRate { get; set; }
    public decimal? VatAmount { get; set; }
    public string? TaxCode { get; set; }
    public bool? TaxInclusive { get; set; }
    public string? PaymentMethod { get; set; }
    public long? PaymentAccountId { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? Description { get; set; }
    public string? AttachmentPath { get; set; }
    public string Status { get; set; } = OtherIncomeStatuses.Draft;
    public long? JournalEntryId { get; set; }
    public long? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public long? UpdatedBy { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public long? SubmittedBy { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public long? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public long? RejectedBy { get; set; }
    public DateTime? RejectedAt { get; set; }
    public string? RejectionReason { get; set; }
    public long? CancelledBy { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancellationReason { get; set; }

    public virtual Business Business { get; set; } = null!;
    public virtual Outlet? Outlet { get; set; }
    public virtual ChartAccount? IncomeAccount { get; set; }
    public virtual ChartAccount? PaymentAccount { get; set; }
    public virtual JournalEntry? JournalEntry { get; set; }
}
