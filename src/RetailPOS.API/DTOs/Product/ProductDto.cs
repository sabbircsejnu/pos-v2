namespace RetailPOS.API.DTOs.Product;

/// <summary>
/// Product data transfer object
/// </summary>
public class ProductDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Sku { get; set; }
    public string? Barcode { get; set; }
    public long CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public decimal BasePrice { get; set; }
    public decimal? CostPrice { get; set; }
    public decimal TaxRate { get; set; }
    public string? ImageUrl { get; set; }
    public bool IsActive { get; set; }
    public bool HasVariants { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    
    // Related data
    public int VariantCount { get; set; }
    public int TotalStock { get; set; }
    public List<ProductVariantDto> Variants { get; set; } = new();
}
