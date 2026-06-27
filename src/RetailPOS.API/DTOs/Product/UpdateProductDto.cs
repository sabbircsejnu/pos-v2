using System.ComponentModel.DataAnnotations;

namespace RetailPOS.API.DTOs.Product;

/// <summary>
/// DTO for updating an existing product
/// </summary>
public class UpdateProductDto
{
    [Required(ErrorMessage = "Product name is required")]
    [StringLength(255, MinimumLength = 2, ErrorMessage = "Product name must be between 2 and 255 characters")]
    public string Name { get; set; } = string.Empty;
    
    [StringLength(1000, ErrorMessage = "Description cannot exceed 1000 characters")]
    public string? Description { get; set; }
    
    [StringLength(50, ErrorMessage = "Product code cannot exceed 50 characters")]
    public string? ProductCode { get; set; }
    
    [StringLength(50, ErrorMessage = "SKU cannot exceed 50 characters")]
    public string? Sku { get; set; }
    
    [StringLength(50, ErrorMessage = "Barcode cannot exceed 50 characters")]
    public string? Barcode { get; set; }
    
    [Required(ErrorMessage = "Category is required")]
    public long CategoryId { get; set; }
    
    [Required(ErrorMessage = "Base price is required")]
    [Range(0, 999999999, ErrorMessage = "Base price must be between 0 and 999,999,999")]
    public decimal BasePrice { get; set; }
    
    [Range(0, 999999999, ErrorMessage = "Cost price must be between 0 and 999,999,999")]
    public decimal? CostPrice { get; set; }
    
    [Range(0, 100, ErrorMessage = "Tax rate must be between 0 and 100")]
    public decimal TaxRate { get; set; }

    /// <summary>
    /// Product status. Accepted values: "active", "inactive", "draft".
    /// </summary>
    public string Status { get; set; } = "active";
}
