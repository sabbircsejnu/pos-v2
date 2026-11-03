using System.ComponentModel.DataAnnotations;

namespace RetailPOS.API.DTOs.Expense;

public class ExpenseCategoryDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public long ExpenseAccountId { get; set; }
    public string? ExpenseAccountCode { get; set; }
    public string? ExpenseAccountName { get; set; }
    public bool IsActive { get; set; }
}

public class UpsertExpenseCategoryDto
{
    [Required, MaxLength(120)] public string Name { get; set; } = string.Empty;
    [Required] public long ExpenseAccountId { get; set; }
    public bool IsActive { get; set; } = true;
}

public class ExpenseDto
{
    public long Id { get; set; }
    public string? ExpenseNumber { get; set; }
    public string Category { get; set; } = string.Empty;
    public long? ExpenseCategoryId { get; set; }
    public long? ExpenseAccountId { get; set; }
    public decimal Amount { get; set; }
    public string? PaymentMethodId { get; set; }
    public long? PaymentAccountId { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? Description { get; set; }
    public string? AttachmentPath { get; set; }
    public DateTime ExpenseDate { get; set; }
    public long? OutletId { get; set; }
    public string? OutletName { get; set; }
    public string Status { get; set; } = string.Empty;
    public long? JournalEntryId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime? RejectedAt { get; set; }
    public string? RejectionReason { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancellationReason { get; set; }

    /// <summary>bank | petty_cash | pos_drawer; null on legacy rows.</summary>
    public string? PaymentSource { get; set; }

    public DateTime? DisbursedAtUtc { get; set; }
    public long? DisbursedCashSessionId { get; set; }
    public bool SelfApprovedUnderOwnerException { get; set; }
    public long? CreatedBy { get; set; }
    public long? SubmittedBy { get; set; }
    public long? ApprovedBy { get; set; }
}

public class CreateExpenseDto
{
    /// <summary>bank | petty_cash | pos_drawer. POS Drawer and Petty Cash require independent approval (D7).</summary>
    [MaxLength(20)] public string? PaymentSource { get; set; }

    [Required] public long ExpenseCategoryId { get; set; }
    [Required, Range(0.01, double.MaxValue)] public decimal Amount { get; set; }
    public decimal? VatRate { get; set; }
    public decimal? VatAmount { get; set; }
    public string? TaxCode { get; set; }
    public bool? TaxInclusive { get; set; }
    [MaxLength(50)] public string? PaymentMethodId { get; set; }
    [Required] public long PaymentAccountId { get; set; }
    [MaxLength(80)] public string? ReferenceNumber { get; set; }
    public string? Description { get; set; }
    [MaxLength(500)] public string? AttachmentPath { get; set; }
    [Required] public DateTime ExpenseDate { get; set; }
    public long? OutletId { get; set; }
}

public class UpdateExpenseDto
{
    [MaxLength(20)] public string? PaymentSource { get; set; }

    [Required] public long ExpenseCategoryId { get; set; }
    [Required, Range(0.01, double.MaxValue)] public decimal Amount { get; set; }
    public decimal? VatRate { get; set; }
    public decimal? VatAmount { get; set; }
    public string? TaxCode { get; set; }
    public bool? TaxInclusive { get; set; }
    [MaxLength(50)] public string? PaymentMethodId { get; set; }
    [Required] public long PaymentAccountId { get; set; }
    [MaxLength(80)] public string? ReferenceNumber { get; set; }
    public string? Description { get; set; }
    [MaxLength(500)] public string? AttachmentPath { get; set; }
    [Required] public DateTime ExpenseDate { get; set; }
    public long? OutletId { get; set; }
}

public class ApproveExpenseDto
{
    public long? ExpenseAccountId { get; set; }
}

public class ExpenseSearchDto
{
    public string? Category { get; set; }
    public string? Status { get; set; }
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
