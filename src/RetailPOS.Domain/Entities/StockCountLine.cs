namespace RetailPOS.Core.Entities;

public class StockCountLine
{
    public long Id { get; set; }
    public long StockCountId { get; set; }
    public long ProductId { get; set; }
    public long VariantId { get; set; }

    public string ProductName { get; set; } = string.Empty;
    public string ProductCode { get; set; } = string.Empty;
    public string VariantName { get; set; } = string.Empty;
    public decimal CurrentStock { get; set; }

    public decimal? PhysicalStock { get; set; }
    public decimal? Difference { get; set; }
    public string? Remarks { get; set; }

    public virtual StockCount StockCount { get; set; } = null!;
    public virtual Product Product { get; set; } = null!;
    public virtual ProductVariant Variant { get; set; } = null!;
}
