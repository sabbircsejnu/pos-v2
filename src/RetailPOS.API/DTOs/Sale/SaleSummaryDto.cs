namespace RetailPOS.API.DTOs.Sale;

public class SaleSummaryDto
{
    public int TotalSales { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal TotalDiscount { get; set; }
    public decimal TotalTax { get; set; }
    public Dictionary<string, decimal> PaymentBreakdown { get; set; } = new();
}
