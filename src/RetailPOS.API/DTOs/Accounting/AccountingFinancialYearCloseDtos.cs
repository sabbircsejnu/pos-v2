using System.ComponentModel.DataAnnotations;

namespace RetailPOS.API.DTOs.Accounting;

public sealed class AccountingFinancialYearCloseRunDto
{
    public long Id { get; set; }
    public long FinancialYearId { get; set; }
    public long FinalAccountingPeriodId { get; set; }
    public long FinalPeriodCloseRunId { get; set; }
    public int RunNumber { get; set; }
    public string Status { get; set; } = string.Empty;
    public long PreparedBy { get; set; }
    public DateTime PreparedAt { get; set; }
    public long? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public long? ExecutedBy { get; set; }
    public DateTime? ExecutedAt { get; set; }
    public long? YearEndJournalEntryId { get; set; }
    public string? YearEndJournalNumber { get; set; }
    public long? ReversalJournalEntryId { get; set; }
    public long? ReopenedBy { get; set; }
    public DateTime? ReopenedAt { get; set; }
    public string? ReopenReason { get; set; }
}

public sealed class ReopenFinancialYearCloseDto
{
    [Required, MaxLength(500)] public string Reason { get; set; } = string.Empty;
    [Required] public DateTime PostingDate { get; set; }
}