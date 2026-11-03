using System.ComponentModel.DataAnnotations;

namespace RetailPOS.API.DTOs.Accounting;

public sealed class CreateBankReconciliationSessionDto
{
    [Range(1, long.MaxValue)] public long BankAccountId { get; set; }
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
}

public sealed class BankReconciliationSessionDto
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long BankAccountId { get; set; }
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public decimal? StatementOpeningBalance { get; set; }
    public decimal? StatementClosingBalance { get; set; }
    public decimal? GlOpeningBalance { get; set; }
    public string Status { get; set; } = string.Empty;
}

public sealed class ReconciliationCandidateDto
{
    public long? BankStatementLineId { get; set; }
    public long? JournalEntryLineId { get; set; }
    public DateTime Date { get; set; }
    public decimal SignedAmount { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? ExternalReference { get; set; }
    public long? JournalEntryId { get; set; }
    public string? JournalNumber { get; set; }
    public string? SourceType { get; set; }
    public string? SourceReference { get; set; }
}

public sealed class CreateBankReconciliationMatchDto
{
    [Range(1, long.MaxValue)] public long SessionId { get; set; }
    public List<long> StatementLineIds { get; set; } = [];
    public List<long> JournalEntryLineIds { get; set; } = [];
}

public sealed class BankReconciliationMatchDto
{
    public long Id { get; set; }
    public long SessionId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal StatementTotal { get; set; }
    public decimal GlTotal { get; set; }
}

public sealed class UnmatchBankReconciliationDto
{
    [Required, MaxLength(500)] public string Reason { get; set; } = string.Empty;
}

public sealed class BankReconciliationCalculationDto
{
    public decimal StatementOpeningBalance { get; set; }
    public decimal StatementMovement { get; set; }
    public decimal StatementClosingBalance { get; set; }
    public decimal GlOpeningBalance { get; set; }
    public decimal GlMovement { get; set; }
    public decimal GlClosingBalance { get; set; }
    public decimal MatchedStatementAmount { get; set; }
    public int MatchedStatementCount { get; set; }
    public decimal MatchedGlAmount { get; set; }
    public int MatchedGlCount { get; set; }
    public decimal UnmatchedStatementAmount { get; set; }
    public int UnmatchedStatementCount { get; set; }
    public decimal UnmatchedGlAmount { get; set; }
    public int UnmatchedGlCount { get; set; }
    public decimal ReconciliationDifference { get; set; }
    public int UnresolvedExceptionCount { get; set; }
}
