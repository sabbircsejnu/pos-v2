using System.ComponentModel.DataAnnotations;

namespace RetailPOS.API.DTOs.Category;

/// <summary>
/// DTO for creating a new category
/// </summary>
public class CreateCategoryDto
{
    [Required(ErrorMessage = "Category name is required")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Category name must be between 2 and 100 characters")]
    public string Name { get; set; } = string.Empty;
    
    [StringLength(500, ErrorMessage = "Description cannot exceed 500 characters")]
    public string? Description { get; set; }
    
    /// <summary>
    /// Parent category ID (null for root categories)
    /// </summary>
    public long? ParentCategoryId { get; set; }
    
    [StringLength(500, ErrorMessage = "Image URL cannot exceed 500 characters")]
    public string? ImageUrl { get; set; }
    
    [Range(0, 9999, ErrorMessage = "Display order must be between 0 and 9999")]
    public int DisplayOrder { get; set; } = 0;
    
    public bool IsActive { get; set; } = true;
}
