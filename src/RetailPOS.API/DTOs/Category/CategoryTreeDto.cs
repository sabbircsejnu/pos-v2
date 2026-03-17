namespace RetailPOS.API.DTOs.Category;

/// <summary>
/// Category with hierarchical children for tree display
/// </summary>
public class CategoryTreeDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public long? ParentCategoryId { get; set; }
    public string? ImageUrl { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; }
    public int Level { get; set; }
    public int ProductCount { get; set; }
    
    /// <summary>
    /// Child categories (recursive structure)
    /// </summary>
    public List<CategoryTreeDto> Children { get; set; } = new();
}
