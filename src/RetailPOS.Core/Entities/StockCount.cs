namespace RetailPOS.Core.Entities;

public class StockCount
{
    public const string StatusDraft = "Draft";
    public const string StatusSubmitted = "Submitted";
    public const string StatusRejected = "Rejected";
    public const string StatusApproved = "Approved";
    public const string StatusAdjustmentGenerated = "AdjustmentGenerated";
    public const string StatusCompleted = "Completed";

    public long Id { get; set; }
    public string StockCountNo { get; set; } = string.Empty;
    public long BusinessId { get; set; }
    public long LocationId { get; set; }
    public string LocationType { get; set; } = string.Empty;
    public DateTime StockCountDate { get; set; }
    public string Status { get; set; } = StatusDraft;
    public string? Remarks { get; set; }
    public int TotalItems { get; set; }

    public long CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public long? SubmittedBy { get; set; }
    public DateTime? SubmittedAt { get; set; }

    public long? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }

    public long? RejectedBy { get; set; }
    public DateTime? RejectedAt { get; set; }
    public string? RejectionReason { get; set; }

    public virtual User Creator { get; set; } = null!;
    public virtual User? Submitter { get; set; }
    public virtual User? Approver { get; set; }
    public virtual User? Rejector { get; set; }
    public virtual ICollection<StockCountLine> Lines { get; set; } = new List<StockCountLine>();
}
