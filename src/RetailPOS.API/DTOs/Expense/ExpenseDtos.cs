using System.ComponentModel.DataAnnotations;

namespace RetailPOS.API.DTOs.Expense;

public class ExpenseDto
{
    public long Id { get; set; }
    public string Category { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? Description { get; set; }
    public DateTime ExpenseDate { get; set; }
    public long? OutletId { get; set; }
    public string? OutletName { get; set; }
}

public class CreateExpenseDto
{
    [Required] public string Category { get; set; } = string.Empty;
    [Required, Range(0.01, double.MaxValue)] public decimal Amount { get; set; }
    public string? Description { get; set; }
    [Required] public DateTime ExpenseDate { get; set; }
    public long? OutletId { get; set; }
}

public class UpdateExpenseDto
{
    [Required] public string Category { get; set; } = string.Empty;
    [Required, Range(0.01, double.MaxValue)] public decimal Amount { get; set; }
    public string? Description { get; set; }
    [Required] public DateTime ExpenseDate { get; set; }
    public long? OutletId { get; set; }
}

public class ExpenseSearchDto
{
    public string? Category { get; set; }
    public long? OutletId { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class ExpenseListDto
{
    public List<ExpenseDto> Expenses { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public decimal TotalAmount { get; set; }
}

public class ExpenseSummaryDto
{
    public decimal TotalAmount { get; set; }
    public Dictionary<string, decimal> ByCategory { get; set; } = new();
    public int Count { get; set; }
}
