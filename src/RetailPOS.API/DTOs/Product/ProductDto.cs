namespace RetailPOS.API.DTOs.Product;

/// <summary>
/// Product data transfer object
/// </summary>
public class ProductDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    /// <summary>Business-facing master product identifier. Separate from the variant-level SKU.</summary>
    public string? ProductCode { get; set; }
    public string? Sku { get; set; }
    public string? Barcode { get; set; }
    public long CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public decimal BasePrice { get; set; }
    public decimal? CostPrice { get; set; }
    public decimal TaxRate { get; set; }
    public string? PrimaryImageThumb { get; set; }
    public string? PrimaryImageMedium { get; set; }
    /// <summary>Canonical status: "active", "inactive", or "draft".</summary>
    public string Status { get; set; } = "active";
    /// <summary>True only when Status is "active". Convenience shorthand for existing consumers.</summary>
    public bool IsActive { get; set; }
    public bool HasVariants { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    
    // Related data
    public int VariantCount { get; set; }
    public int TotalStock { get; set; }
    public List<ProductVariantDto> Variants { get; set; } = new();
}
