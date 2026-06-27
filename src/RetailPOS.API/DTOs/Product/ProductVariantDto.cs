namespace RetailPOS.API.DTOs.Product;

/// <summary>
/// Product variant data transfer object
/// </summary>
public class ProductVariantDto
{
    public long Id { get; set; }
    public long ProductId { get; set; }
    /// <summary>Main product code from the parent product. Included so callers never need a second lookup.</summary>
    public string? ProductCode { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Sku { get; set; }
    public string? Barcode { get; set; }
    public string? Attributes { get; set; } // JSON string
    public decimal PriceAdjustment { get; set; }
    public decimal FinalPrice { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
