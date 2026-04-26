namespace RetailPOS.API.DTOs.Sale;

public class SaleDto
{
    public long Id { get; set; }
    public string SaleNumber { get; set; } = string.Empty;  // UPDATED
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
    /// <summary>TotalAmount − Discount + Tax (net payable amount).</summary>
    public decimal NetTotal { get; set; }                   // UPDATED
    public string PaymentMethod { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public List<SaleItemDto> Items { get; set; } = new();
    public List<SalePaymentDto> Payments { get; set; } = new(); // UPDATED — split-payment breakdown
}

// UPDATED — split-payment line in the response
public class SalePaymentDto
{
    public long    Id       { get; set; }
    public string  Method   { get; set; } = string.Empty;
    public decimal Amount   { get; set; }
    public decimal? Tendered { get; set; }
    /// <summary>For cash payments: Tendered – Amount (0 for non-cash).</summary>
    public decimal Change   { get; set; }
}

