namespace RetailPOS.Core.Entities;

public class BarcodePrintHistoryItem
{
    public long Id { get; set; }
    public long PrintHistoryId { get; set; }
    public long VariantId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string VariantName { get; set; } = string.Empty;
    public string VariantSku { get; set; } = string.Empty;
    public string? BarcodeValue { get; set; }
    public string? VariantAttributes { get; set; }
    public decimal? SellingPrice { get; set; }
    public int QuantityPrinted { get; set; }

    // Navigation properties
    public virtual BarcodePrintHistory PrintHistory { get; set; } = null!;
    public virtual ProductVariant Variant { get; set; } = null!;
}
