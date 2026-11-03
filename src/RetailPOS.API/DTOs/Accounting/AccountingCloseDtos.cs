using System.ComponentModel.DataAnnotations;

namespace RetailPOS.API.DTOs.Accounting;

public sealed class AccountingPeriodCloseRunDto
{
    public long Id { get; set; }
    public long AccountingPeriodId { get; set; }
    public int RunNumber { get; set; }
    public string Status { get; set; } = string.Empty;
    public int EvaluationSequence { get; set; }
    public long PreparedBy { get; set; }
    public DateTime PreparedAt { get; set; }
    public long? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public long? ExecutedBy { get; set; }
    public DateTime? ExecutedAt { get; set; }
    public string? ExecutionReason { get; set; }
    public long? ReopenedBy { get; set; }
    public DateTime? ReopenedAt { get; set; }
    public string? ReopenReason { get; set; }
    public List<AccountingCloseGateSnapshotDto> Gates { get; set; } = new();
}

public sealed class AccountingCloseGateSnapshotDto
{
    public long Id { get; set; }
    public int EvaluationSequence { get; set; }
    public string GateCode { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal? ExpectedAmount { get; set; }
    public decimal? ActualAmount { get; set; }
    public string? Details { get; set; }
    public bool IsHistoricalCutoverLimitation { get; set; }
    public DateTime EvaluatedAt { get; set; }
    public bool HasApprovedException { get; set; }
    public string? ApprovedExceptionReason { get; set; }
    public long? ExceptionApprovedBy { get; set; }
    public DateTime? ExceptionApprovedAt { get; set; }
}

public sealed class CloseRunReasonDto { [MaxLength(500)] public string? Reason { get; set; } }
public sealed class ApproveCloseExceptionDto { [Required, MaxLength(500)] public string Reason { get; set; } = string.Empty; }