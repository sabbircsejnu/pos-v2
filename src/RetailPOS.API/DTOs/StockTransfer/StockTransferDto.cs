namespace RetailPOS.API.DTOs.StockTransfer;

public class StockTransferDto
{
    public long Id { get; set; }
    public string? TransferNo { get; set; }
    public string TransferType { get; set; } = "direct";
    public long? RelatedRequisitionId { get; set; }
    public long FromLocationId { get; set; }
    public string FromLocationType { get; set; } = string.Empty;
    public string FromLocationName { get; set; } = string.Empty;
    public long ToLocationId { get; set; }
    public string ToLocationType { get; set; } = string.Empty;
    public string ToLocationName { get; set; } = string.Empty;
    public DateTime TransferDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public long? ApprovedBy { get; set; }
    public string? ApproverName { get; set; }
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
    public string? CreatorName { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<StockTransferItemDto> Items { get; set; } = new();
}
