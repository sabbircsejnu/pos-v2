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

    /// <summary>
    /// None, DraftGenerated, Completed (manual draft path) or the asynchronous job state:
    /// Pending, Processing, RetryScheduled, Completed, Blocked, Failed. "Approved" with a Pending job means
    /// AdjustmentPending - Inventory has not changed yet.
    /// </summary>
    public string AdjustmentState { get; set; } = "None";
    public StockCountAdjustmentJobDto? AdjustmentJob { get; set; }

    public List<StockCountLineDto> Lines { get; set; } = new();
}

public class StockCountAdjustmentJobDto
{
    public Guid JobId { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool IsTerminal { get; set; }
    public int AttemptCount { get; set; }
    public DateTime? NextAttemptAtUtc { get; set; }
    public string? FailureCode { get; set; }
    public string? EvidenceStatus { get; set; }
    public List<string> BlockedReasonCodes { get; set; } = new();
    public long? StockAdjustmentId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}
