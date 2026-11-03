namespace RetailPOS.Core.Entities;

public class StockTransfer
{
    public const string StatusDraft = "draft";
    public const string StatusSubmitted = "submitted";
    public const string StatusApproved = "approved";
    public const string StatusInTransit = "in_transit";
    public const string StatusReceived = "received";
    public const string StatusPartiallyReceived = "partially_received";
    public const string StatusRejected = "rejected";
    public const string StatusCancelled = "cancelled";

    public const string TransferTypeDirect = "direct";
    public const string TransferTypeRequisition = "requisition";
    public const string TransferTypeReturn = "return";

    public long Id { get; set; }
    public string? TransferNo { get; set; }
    public string TransferType { get; set; } = TransferTypeDirect;
    public long FromLocationId { get; set; }
    public string FromLocationType { get; set; } = string.Empty;
    public long ToLocationId { get; set; }
    public string ToLocationType { get; set; } = string.Empty;
    public long? RelatedRequisitionId { get; set; }
    public string? Notes { get; set; }
    public DateTime TransferDate { get; set; }
    public string Status { get; set; } = "pending";
    public long? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public long? SubmittedBy { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public long? DispatchedBy { get; set; }
    public DateTime? DispatchedAt { get; set; }
    public long? ReceivedBy { get; set; }
    public DateTime? ReceivedAt { get; set; }
    public long? RejectedBy { get; set; }
    public DateTime? RejectedAt { get; set; }
    public long? CancelledBy { get; set; }
    public DateTime? CancelledAt { get; set; }
    public long? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual User? Approver { get; set; }
    public virtual User? Submitter { get; set; }
    public virtual User? Dispatcher { get; set; }
    public virtual User? Receiver { get; set; }
    public virtual User? Rejector { get; set; }
    public virtual User? Canceller { get; set; }
    public virtual User? Updater { get; set; }
    public virtual User? Creator { get; set; }
    public virtual StockRequisition? RelatedRequisition { get; set; }
    public virtual ICollection<StockTransferItem> Items { get; set; } = new List<StockTransferItem>();
}
