namespace RetailPOS.API.DTOs.StockCount;

public class StockCountDto
{
    public long Id { get; set; }
    public string StockCountNo { get; set; } = string.Empty;
    public long BusinessId { get; set; }
    public long LocationId { get; set; }
    public string LocationType { get; set; } = string.Empty;
    public string LocationName { get; set; } = string.Empty;
    public DateTime StockCountDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Remarks { get; set; }
    public int TotalItems { get; set; }
    public long CreatedBy { get; set; }
    public string CreatedByName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public long? SubmittedBy { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public long? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public long? RejectedBy { get; set; }
    public DateTime? RejectedAt { get; set; }
    public string? RejectionReason { get; set; }

    public bool HasPostGenerationMovements { get; set; }
    public int PostGenerationMovementCount { get; set; }
    public DateTime? LastPostGenerationMovementAt { get; set; }

    public List<StockCountLineDto> Lines { get; set; } = new();
}
