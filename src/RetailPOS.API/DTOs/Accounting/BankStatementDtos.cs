using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace RetailPOS.API.DTOs.Accounting;

public sealed class BankStatementImportRequest
{
    [Range(1, long.MaxValue)] public long BankAccountId { get; set; }
    public IFormFile? File { get; set; }
}

public sealed class BankStatementImportDto
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long BankAccountId { get; set; }
    public string SourceFileName { get; set; } = string.Empty;
    public string SourceFileChecksum { get; set; } = string.Empty;
    public string SourceFormat { get; set; } = string.Empty;
    public DateTime ImportedAt { get; set; }
    public DateTime? StatementPeriodStart { get; set; }
    public DateTime? StatementPeriodEnd { get; set; }
    public decimal? OpeningBalance { get; set; }
    public decimal? ClosingBalance { get; set; }
    public int ImportedRowCount { get; set; }
    public int AcceptedRowCount { get; set; }
    public int RejectedRowCount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ErrorSummary { get; set; }
    public long? CreatedBy { get; set; }
}

public sealed class BankStatementLineDto
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long BankAccountId { get; set; }
    public long BankStatementImportId { get; set; }
    public int SourceRowNumber { get; set; }
    public DateTime TransactionDate { get; set; }
    public DateTime? ValueDate { get; set; }
    public string? ExternalTransactionReference { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal DebitAmount { get; set; }
    public decimal CreditAmount { get; set; }
    public decimal SignedAmount { get; set; }
    public decimal? RunningBalance { get; set; }
    public string? Currency { get; set; }
    public string? RawReferenceText { get; set; }
}
