namespace RetailPOS.API.DTOs.Category;

/// <summary>
/// DTO for moving a category to a different parent
/// </summary>
public class MoveCategoryDto
{
    /// <summary>
    /// New parent category ID (null to move to root level)
    /// </summary>
    public long? NewParentCategoryId { get; set; }
    
    /// <summary>
    /// New display order (optional)
    /// </summary>
    public int? NewDisplayOrder { get; set; }
}
