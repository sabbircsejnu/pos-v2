using RetailPOS.Core.Entities;

namespace RetailPOS.Infrastructure.Repositories;

/// <summary>
/// Repository interface for Category operations
/// </summary>
public interface ICategoryRepository
{
    /// <summary>
    /// Get category by ID
    /// </summary>
    Task<Category?> GetByIdAsync(long id);
    
    /// <summary>
    /// Get category by name
    /// </summary>
    Task<Category?> GetByNameAsync(string name);
    
    /// <summary>
    /// Get all categories (flat list)
    /// </summary>
    Task<IEnumerable<Category>> GetAllAsync();
    
    /// <summary>
    /// Get all root categories (no parent)
    /// </summary>
    Task<IEnumerable<Category>> GetRootCategoriesAsync();
    
    /// <summary>
    /// Get all children of a specific category
    /// </summary>
    Task<IEnumerable<Category>> GetChildrenAsync(long parentCategoryId);
    
    /// <summary>
    /// Get all descendants (recursive) of a category
    /// </summary>
    Task<IEnumerable<Category>> GetAllDescendantsAsync(long parentCategoryId);
    
    /// <summary>
    /// Create new category
    /// </summary>
    Task<Category> CreateAsync(Category category);
    
    /// <summary>
    /// Update existing category
    /// </summary>
    Task UpdateAsync(Category category);
    
    /// <summary>
    /// Delete category
    /// </summary>
    Task DeleteAsync(long id);
    
    /// <summary>
    /// Check if category name exists (for uniqueness validation)
    /// </summary>
    Task<bool> ExistsAsync(string name, long? excludeId = null);
    
    /// <summary>
    /// Check if category has children
    /// </summary>
    Task<bool> HasChildrenAsync(long categoryId);
    
    /// <summary>
    /// Check if category has products
    /// </summary>
    Task<bool> HasProductsAsync(long categoryId);
    
    /// <summary>
    /// Get category count by parent
    /// </summary>
    Task<int> GetChildCountAsync(long parentCategoryId);
    
    /// <summary>
    /// Get product count by category
    /// </summary>
    Task<int> GetProductCountAsync(long categoryId);
}
