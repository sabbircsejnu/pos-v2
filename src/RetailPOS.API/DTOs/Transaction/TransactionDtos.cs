using System.ComponentModel.DataAnnotations;

namespace RetailPOS.API.DTOs.Transaction;

public class TransactionDto
{
    public long Id { get; set; }
    public long AccountId { get; set; }
    public string AccountName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Type { get; set; } = string.Empty; // debit, credit
    public string? Description { get; set; }
    public DateTime TransactionDate { get; set; }
    public string? ReferenceType { get; set; }
    public long? ReferenceId { get; set; }
}

public class CreateTransactionDto
{
    [Required] public long AccountId { get; set; }
    [Required, Range(0.01, double.MaxValue)] public decimal Amount { get; set; }
    [Required] public string Type { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime? TransactionDate { get; set; }
    public string? ReferenceType { get; set; }
    public long? ReferenceId { get; set; }
}

public class TransactionSearchDto
{
    public long? AccountId { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string? Type { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class TransactionListDto
{
    public List<TransactionDto> Transactions { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
}
