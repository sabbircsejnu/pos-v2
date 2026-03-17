namespace RetailPOS.API.DTOs.Category;

/// <summary>
/// Category data transfer object
/// </summary>
public class CategoryDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public long? ParentCategoryId { get; set; }
    public string? ParentCategoryName { get; set; }
    public string? ImageUrl { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    
    // Computed properties
    public int Level { get; set; }
    public int ChildCount { get; set; }
    public int ProductCount { get; set; }
}
