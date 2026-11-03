using System.ComponentModel.DataAnnotations;

namespace RetailPOS.API.DTOs.Accounting;

public sealed class PrepareCloseDto { }
public sealed class CloseReconciliationDto { [Required, MaxLength(500)] public string Reason { get; set; } = string.Empty; }
public sealed class ReopenReconciliationDto { [Required, MaxLength(500)] public string Reason { get; set; } = string.Empty; }
public sealed class CreateStatementAdjustmentDto
{
    [Range(1, long.MaxValue)] public long BankStatementLineId { get; set; }
    [Required] public string AdjustmentType { get; set; } = string.Empty;
    [Range(typeof(decimal), "0.01", "79228162514264337593543950335")] public decimal Amount { get; set; }
    [Required, MaxLength(500)] public string Reason { get; set; } = string.Empty;
    [Required, MaxLength(120)] public string IdempotencyKey { get; set; } = string.Empty;
    public DateTime PostingDate { get; set; }
}
public sealed class ApproveStatementAdjustmentDto { [Required, MaxLength(500)] public string Reason { get; set; } = string.Empty; }
public sealed class ReverseStatementAdjustmentDto { [Required, MaxLength(500)] public string Reason { get; set; } = string.Empty; public DateTime PostingDate { get; set; } }
public sealed class StatementExceptionDto { public long Id { get; set; } public long BankStatementLineId { get; set; } public string Classification { get; set; } = string.Empty; public string Status { get; set; } = string.Empty; public string? Reason { get; set; } public string? ResolutionNotes { get; set; } public long CreatedBy { get; set; } public DateTime CreatedAt { get; set; } public long? ResolvedBy { get; set; } public DateTime? ResolvedAt { get; set; } }
public sealed class ReconciliationMatchDetailDto { public long Id { get; set; } public long SessionId { get; set; } public string Status { get; set; } = string.Empty; public decimal StatementTotal { get; set; } public decimal GlTotal { get; set; } public List<long> StatementLineIds { get; set; } = new List<long>(); public List<long> JournalEntryLineIds { get; set; } = new List<long>(); }
public sealed class StatementAdjustmentDto { public long Id { get; set; } public long BankStatementLineId { get; set; } public string AdjustmentType { get; set; } = string.Empty; public decimal Amount { get; set; } public string Status { get; set; } = string.Empty; public long? JournalEntryId { get; set; } public long? ReversalJournalEntryId { get; set; } }
public sealed class CreateStatementExceptionDto { [Required] public string Classification { get; set; } = string.Empty; [Required, MaxLength(500)] public string Reason { get; set; } = string.Empty; }
public sealed class ResolveStatementExceptionDto { [Required, MaxLength(1000)] public string ResolutionNotes { get; set; } = string.Empty; }
