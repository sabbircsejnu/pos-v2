namespace RetailPOS.API.DTOs.StockCount;

public class StockCountLineDto
{
    public long Id { get; set; }
    public long ProductId { get; set; }
    public long VariantId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string ProductCode { get; set; } = string.Empty;
    public string VariantName { get; set; } = string.Empty;
    public decimal CurrentStock { get; set; }
    public decimal? PhysicalStock { get; set; }
    public decimal? Difference { get; set; }
    public string? Remarks { get; set; }
}
