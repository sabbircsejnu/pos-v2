namespace RetailPOS.Core.Entities;

public class Product
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    /// <summary>
    /// Business-facing master product identifier (e.g. "SHIRT-001").
    /// Distinct from the optional Sku field and from variant-level SKUs.
    /// Unique when provided; nullable for backwards-compatibility with existing records.
    /// </summary>
    public string? ProductCode { get; set; }
    public string? Sku { get; set; }
    public string? Barcode { get; set; }
    public long CategoryId { get; set; }
    public decimal BasePrice { get; set; } = 0;
    public decimal CostPrice { get; set; } = 0;
    public decimal TaxRate { get; set; } = 0;
    public bool HasVariants { get; set; } = false;

    /// <summary>
    /// The workflow/publication status of the product.
    /// Active = available in POS; Inactive = hidden; Draft = in preparation.
    /// </summary>
    public ProductStatus Status { get; set; } = ProductStatus.Active;

    /// <summary>
    /// Convenience computed flag — true only when Status is Active.
    /// Not persisted to the database. Use Status for LINQ-to-SQL predicates.
    /// </summary>
    public bool IsActive => Status == ProductStatus.Active;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual Category Category { get; set; } = null!;
    public virtual ICollection<ProductVariant> ProductVariants { get; set; } = new List<ProductVariant>();
    public virtual ICollection<ProductVariation> ProductVariations { get; set; } = new List<ProductVariation>();
    public virtual ICollection<ProductImage> Images { get; set; } = new List<ProductImage>();
}
