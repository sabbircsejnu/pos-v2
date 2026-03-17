using System.ComponentModel.DataAnnotations;

namespace RetailPOS.API.DTOs.Bill;

public class BillDto
{
    public long Id { get; set; }
    public long SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public long? PoId { get; set; }
    public decimal AmountDue { get; set; }
    public DateTime DueDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool IsOverdue { get; set; }
}

public class CreateBillDto
{
    [Required] public long SupplierId { get; set; }
    public long? PoId { get; set; }
    [Required, Range(0.01, double.MaxValue)] public decimal AmountDue { get; set; }
    [Required] public DateTime DueDate { get; set; }
}

public class UpdateBillStatusDto
{
    [Required] public string Status { get; set; } = string.Empty; // unpaid, partial, paid
}

public class BillSearchDto
{
    public string? Status { get; set; }
    public long? SupplierId { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class BillListDto
{
    public List<BillDto> Bills { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
}

public class BillSummaryDto
{
    public decimal TotalUnpaid { get; set; }
    public decimal TotalOverdue { get; set; }
    public int UnpaidCount { get; set; }
    public int OverdueCount { get; set; }
}
