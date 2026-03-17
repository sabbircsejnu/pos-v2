using RetailPOS.API.DTOs.Category;

namespace RetailPOS.API.Services;

/// <summary>
/// Service interface for Category business logic
/// </summary>
public interface ICategoryService
{
    /// <summary>
    /// Get category by ID
    /// </summary>
    Task<CategoryDto?> GetCategoryByIdAsync(long id);
    
    /// <summary>
    /// Get all categories as flat list
    /// </summary>
    Task<IEnumerable<CategoryDto>> GetAllCategoriesAsync();
    
    /// <summary>
    /// Get all categories as hierarchical tree
    /// </summary>
    Task<IEnumerable<CategoryTreeDto>> GetCategoryTreeAsync();
    
    /// <summary>
    /// Get children of a specific category
    /// </summary>
    Task<IEnumerable<CategoryDto>> GetChildrenAsync(long parentCategoryId);
    
    /// <summary>
    /// Create new category
    /// </summary>
    Task<CategoryDto> CreateCategoryAsync(CreateCategoryDto dto);
    
    /// <summary>
    /// Update existing category
    /// </summary>
    Task<CategoryDto> UpdateCategoryAsync(long id, UpdateCategoryDto dto);
    
    /// <summary>
    /// Delete category
    /// </summary>
    Task DeleteCategoryAsync(long id);
    
    /// <summary>
    /// Move category to a different parent
    /// </summary>
    Task<CategoryDto> MoveCategoryAsync(long id, MoveCategoryDto dto);
}
