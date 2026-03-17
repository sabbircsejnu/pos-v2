namespace RetailPOS.API.DTOs.Sale;

public class SaleDto
{
    public long Id { get; set; }
    public long OutletId { get; set; }
    public string OutletName { get; set; } = string.Empty;
    public long? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public long CashierId { get; set; }
    public string CashierName { get; set; } = string.Empty;
    public DateTime SaleDate { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal Discount { get; set; }
    public decimal Tax { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public List<SaleItemDto> Items { get; set; } = new();
}
