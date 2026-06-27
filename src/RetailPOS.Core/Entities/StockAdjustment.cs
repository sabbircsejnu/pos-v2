namespace RetailPOS.Core.Entities;

public class StockAdjustment
{
    public const string StatusDraft = "Draft";
    public const string StatusPendingApproval = "PendingApproval";
    public const string StatusApproved = "Approved";
    public const string StatusRejected = "Rejected";
    public const string StatusCancelled = "Cancelled";

    public long Id { get; set; }
    public string AdjustmentNumber { get; set; } = string.Empty;
    public string Status { get; set; } = StatusDraft;
    public long LocationId { get; set; }
    public string LocationType { get; set; } = string.Empty;

    public long AdjustedBy { get; set; }
    public DateTime AdjustmentDate { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SubmittedAt { get; set; }
    public long? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public long? RejectedBy { get; set; }
    public DateTime? RejectedAt { get; set; }
    public string? RejectionReason { get; set; }
    public long? CancelledBy { get; set; }
    public DateTime? CancelledAt { get; set; }

    // Navigation properties
    public virtual User Adjuster { get; set; } = null!;
    public virtual User? Approver { get; set; }
    public virtual User? Rejector { get; set; }
    public virtual User? Canceller { get; set; }
    public virtual ICollection<StockAdjustmentLine> Lines { get; set; } = new List<StockAdjustmentLine>();
}
