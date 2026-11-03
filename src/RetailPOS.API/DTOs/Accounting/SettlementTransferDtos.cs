using System.ComponentModel.DataAnnotations;

namespace RetailPOS.API.DTOs.Accounting;

public sealed class CreateSettlementBatchDto
{
    [Required, MaxLength(30)] public string SettlementType { get; set; } = string.Empty;
    [Range(1, long.MaxValue)] public long ClearingAccountId { get; set; }
    [Range(1, long.MaxValue)] public long BankAccountId { get; set; }
    public DateTime SettlementDate { get; set; }
    [Range(typeof(decimal), "0.01", "79228162514264337593543950335")] public decimal GrossClearingAmount { get; set; }
    [Range(typeof(decimal), "0", "79228162514264337593543950335")] public decimal SettlementFeeAmount { get; set; }
    [MaxLength(120)] public string? ExternalReference { get; set; }
    [Required, MaxLength(120)] public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed class ReverseSettlementBatchDto
{
    [Required, MaxLength(500)] public string Reason { get; set; } = string.Empty;
    public DateTime PostingDate { get; set; }
}

public sealed class SettlementBatchDto
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public string SettlementType { get; set; } = string.Empty;
    public long ClearingAccountId { get; set; }
    public long BankAccountId { get; set; }
    public DateTime SettlementDate { get; set; }
    public decimal GrossClearingAmount { get; set; }
    public decimal SettlementFeeAmount { get; set; }
    public decimal NetBankAmount { get; set; }
    public string? ExternalReference { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public long? JournalEntryId { get; set; }
    public long? ReversalJournalEntryId { get; set; }
}

public sealed class CreateLiquidityTransferDto
{
    [Required, MaxLength(30)] public string TransferType { get; set; } = string.Empty;
    [Range(1, long.MaxValue)] public long? SourceBankAccountId { get; set; }
    [Range(1, long.MaxValue)] public long? DestinationBankAccountId { get; set; }
    [Range(typeof(decimal), "0.01", "79228162514264337593543950335")] public decimal Amount { get; set; }
    public DateTime PostingDate { get; set; }
    [MaxLength(120)] public string? Reference { get; set; }
    [Required, MaxLength(120)] public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed class ReverseLiquidityTransferDto
{
    [Required, MaxLength(500)] public string Reason { get; set; } = string.Empty;
    public DateTime PostingDate { get; set; }
}

public sealed class LiquidityTransferDto
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public string TransferType { get; set; } = string.Empty;
    public long SourceAccountId { get; set; }
    public long DestinationAccountId { get; set; }
    public long? SourceBankAccountId { get; set; }
    public long? DestinationBankAccountId { get; set; }
    public decimal Amount { get; set; }
    public DateTime PostingDate { get; set; }
    public string? Reference { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public long? JournalEntryId { get; set; }
    public long? ReversalJournalEntryId { get; set; }
}
