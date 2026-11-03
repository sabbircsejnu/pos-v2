namespace RetailPOS.Core.Entities;

public class Product
{
    public long Id { get; set; }

    /// <summary>Owning Business; must equal the Business of the product's category.</summary>
    public long BusinessId { get; set; }
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

    /// <summary>
    /// Product-specific tax rate (percent). <c>null</c> means "not configured" and the Business default applies;
    /// an explicit <c>0</c> is a configured 0% rate and is never treated as missing.
    /// </summary>
    public decimal? TaxRate { get; set; }

    /// <summary>
    /// Optional tax classification: <c>standard</c>, <c>zero_rated</c> or <c>exempt</c>. Exempt and zero-rated
    /// products are charged 0% regardless of <see cref="TaxRate"/>; <c>null</c> leaves the choice to the rate.
    /// </summary>
    public string? TaxClassification { get; set; }
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
