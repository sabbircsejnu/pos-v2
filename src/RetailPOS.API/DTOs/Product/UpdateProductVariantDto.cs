using System.ComponentModel.DataAnnotations;

namespace RetailPOS.API.DTOs.Product;

/// <summary>
/// DTO for updating an existing product variant
/// </summary>
public class UpdateProductVariantDto
{
    [Required(ErrorMessage = "Variant name is required")]
    [StringLength(255, MinimumLength = 2, ErrorMessage = "Variant name must be between 2 and 255 characters")]
    public string Name { get; set; } = string.Empty;
    
    [StringLength(50, ErrorMessage = "SKU cannot exceed 50 characters")]
    public string? Sku { get; set; }
    
    [StringLength(50, ErrorMessage = "Barcode cannot exceed 50 characters")]
    public string? Barcode { get; set; }
    
    /// <summary>
    /// JSON string of attributes (e.g., {"size": "L", "color": "Red"})
    /// </summary>
    [StringLength(1000, ErrorMessage = "Attributes cannot exceed 1000 characters")]
    public string? Attributes { get; set; }
    
    [Range(-999999999, 999999999, ErrorMessage = "Price adjustment must be between -999,999,999 and 999,999,999")]
    public decimal PriceAdjustment { get; set; }
}
