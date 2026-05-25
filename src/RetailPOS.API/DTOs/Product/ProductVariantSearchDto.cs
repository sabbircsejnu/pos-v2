namespace RetailPOS.API.DTOs.Product;

/// <summary>
/// Product variant search result DTO for dropdowns
/// </summary>
public class ProductVariantSearchDto
{
    public long Id { get; set; }
    public long ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Sku { get; set; }
    public string? Barcode { get; set; }
    public string? Attributes { get; set; }
    public decimal FinalPrice { get; set; }
    public decimal? CostPrice { get; set; }
    public int? StockQuantity { get; set; }
    public string? PrimaryImageThumb { get; set; }
}
