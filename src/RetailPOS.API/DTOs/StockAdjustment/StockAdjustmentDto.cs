namespace RetailPOS.API.DTOs.StockAdjustment;

public class StockAdjustmentDto
{
    public long Id { get; set; }
    public string AdjustmentNumber { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public long LocationId { get; set; }
    public string LocationType { get; set; } = string.Empty;
    public string LocationName { get; set; } = string.Empty;
    public long VariantId { get; set; }
    public string VariantSku { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public int PreviousQuantity { get; set; }
    public int QuantityChange { get; set; }
    public int NewQuantity { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public int LineCount { get; set; }
    public int TotalIncrease { get; set; }
    public int TotalDecrease { get; set; }
    public int NetQuantityChange { get; set; }
    public List<StockAdjustmentLineDto> Lines { get; set; } = new();
    public long AdjustedBy { get; set; }
    public string AdjusterName { get; set; } = string.Empty;
    public DateTime AdjustmentDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public long? ApprovedBy { get; set; }
    public string? ApprovedByName { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public long? RejectedBy { get; set; }
    public string? RejectedByName { get; set; }
    public DateTime? RejectedAt { get; set; }
    public string? RejectionReason { get; set; }
    public long? CancelledBy { get; set; }
    public string? CancelledByName { get; set; }
    public DateTime? CancelledAt { get; set; }
}
