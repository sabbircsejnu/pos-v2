namespace RetailPOS.API.DTOs.StockRequisition;

public class StockRequisitionDto
{
    public long Id { get; set; }
    public string RequisitionNo { get; set; } = string.Empty;
    public long RequestingLocationId { get; set; }
    public string RequestingLocationType { get; set; } = string.Empty;
    public string RequestingLocationName { get; set; } = string.Empty;
    public long SourceLocationId { get; set; }
    public string SourceLocationType { get; set; } = string.Empty;
    public string SourceLocationName { get; set; } = string.Empty;
    public long RequestedBy { get; set; }
    public string? RequesterName { get; set; }
    public DateTime RequestDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
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
    public List<StockRequisitionLineDto> Lines { get; set; } = new();
}

public class StockRequisitionLineDto
{
    public long Id { get; set; }
    public long VariantId { get; set; }
    public string VariantSku { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public int RequestedQuantity { get; set; }
    public int FulfilledQuantity { get; set; }
    public int PendingQuantity => RequestedQuantity - FulfilledQuantity;
    public string? Remarks { get; set; }
}
