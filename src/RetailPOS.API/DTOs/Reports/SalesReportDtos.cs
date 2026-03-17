namespace RetailPOS.API.DTOs.Reports;

public class SalesReportDto
{
    public decimal TotalRevenue { get; set; }
    public decimal TotalDiscount { get; set; }
    public decimal TotalTax { get; set; }
    public decimal NetRevenue { get; set; }
    public int TotalTransactions { get; set; }
    public decimal AverageOrderValue { get; set; }
}

public class TopProductDto
{
    public long VariantId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public int TotalQuantity { get; set; }
    public decimal TotalRevenue { get; set; }
}

public class SalesByOutletDto
{
    public long OutletId { get; set; }
    public string OutletName { get; set; } = string.Empty;
    public int TransactionCount { get; set; }
    public decimal TotalRevenue { get; set; }
}

public class SalesByPaymentMethodDto
{
    public string PaymentMethod { get; set; } = string.Empty;
    public int Count { get; set; }
    public decimal Amount { get; set; }
}

public class DailySalesTrendDto
{
    public DateTime Date { get; set; }
    public int TransactionCount { get; set; }
    public decimal Revenue { get; set; }
}

public class SalesReportFilterDto
{
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public long? OutletId { get; set; }
    public string? PaymentMethod { get; set; }
}
