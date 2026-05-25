namespace RetailPOS.Core.Entities;

public class Product
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Sku { get; set; }
    public string? Barcode { get; set; }
    public long CategoryId { get; set; }
    public decimal BasePrice { get; set; } = 0;
    public decimal CostPrice { get; set; } = 0;
    public decimal TaxRate { get; set; } = 0;
    public bool HasVariants { get; set; } = false;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual Category Category { get; set; } = null!;
    public virtual ICollection<ProductVariant> ProductVariants { get; set; } = new List<ProductVariant>();
    public virtual ICollection<ProductVariation> ProductVariations { get; set; } = new List<ProductVariation>();
    public virtual ICollection<ProductImage> Images { get; set; } = new List<ProductImage>();
}
