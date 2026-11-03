namespace RetailPOS.Core.Entities;

public class StockRequisition
{
    public const string StatusDraft = "draft";
    public const string StatusSubmitted = "submitted";
    public const string StatusApproved = "approved";
    public const string StatusRejected = "rejected";
    public const string StatusPartiallyFulfilled = "partially_fulfilled";
    public const string StatusFullyFulfilled = "fully_fulfilled";
    public const string StatusClosed = "closed";

    public long Id { get; set; }
    public string RequisitionNo { get; set; } = string.Empty;
    public long RequestingLocationId { get; set; }
    public string RequestingLocationType { get; set; } = string.Empty;
    public long SourceLocationId { get; set; }
    public string SourceLocationType { get; set; } = string.Empty;
    public long RequestedBy { get; set; }
    public DateTime RequestDate { get; set; } = DateTime.UtcNow;
    public string Status { get; set; } = StatusDraft;
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public long? SubmittedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public long? ApprovedBy { get; set; }
    public DateTime? RejectedAt { get; set; }
    public long? RejectedBy { get; set; }
    public string? RejectionReason { get; set; }
    public DateTime? ClosedAt { get; set; }
    public long? ClosedBy { get; set; }

    // Navigation properties
    public virtual User Requester { get; set; } = null!;
    public virtual User? Submitter { get; set; }
    public virtual User? Approver { get; set; }
    public virtual User? Rejector { get; set; }
    public virtual User? Closer { get; set; }
    public virtual User? Updater { get; set; }
    public virtual ICollection<StockRequisitionLine> Lines { get; set; } = new List<StockRequisitionLine>();
    public virtual ICollection<StockTransfer> Transfers { get; set; } = new List<StockTransfer>();
}
