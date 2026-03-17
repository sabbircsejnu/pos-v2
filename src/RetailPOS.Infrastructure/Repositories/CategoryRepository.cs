using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Repositories;

/// <summary>
/// Repository implementation for Category operations
/// </summary>
public class CategoryRepository : ICategoryRepository
{
    private readonly RetailPOSDbContext _context;

    public CategoryRepository(RetailPOSDbContext context)
    {
        _context = context;
    }

    public async Task<Category?> GetByIdAsync(long id)
    {
        return await _context.Categories
            .Include(c => c.ParentCategory)
            .Include(c => c.ChildCategories)
            .Include(c => c.Products)
            .FirstOrDefaultAsync(c => c.Id == id);
    }

    public async Task<Category?> GetByNameAsync(string name)
    {
        return await _context.Categories
            .FirstOrDefaultAsync(c => c.Name.ToLower() == name.ToLower());
    }

    public async Task<IEnumerable<Category>> GetAllAsync()
    {
        return await _context.Categories
            .Include(c => c.ParentCategory)
            .OrderBy(c => c.DisplayOrder)
            .ThenBy(c => c.Name)
            .ToListAsync();
    }

    public async Task<IEnumerable<Category>> GetRootCategoriesAsync()
    {
        return await _context.Categories
            .Where(c => c.ParentCategoryId == null)
            .OrderBy(c => c.DisplayOrder)
            .ThenBy(c => c.Name)
            .ToListAsync();
    }

    public async Task<IEnumerable<Category>> GetChildrenAsync(long parentCategoryId)
    {
        return await _context.Categories
            .Where(c => c.ParentCategoryId == parentCategoryId)
            .OrderBy(c => c.DisplayOrder)
            .ThenBy(c => c.Name)
            .ToListAsync();
    }

    public async Task<IEnumerable<Category>> GetAllDescendantsAsync(long parentCategoryId)
    {
        // Get all descendants recursively using SQL
        var descendants = new List<Category>();
        var children = await GetChildrenAsync(parentCategoryId);
        
        foreach (var child in children)
        {
            descendants.Add(child);
            var childDescendants = await GetAllDescendantsAsync(child.Id);
            descendants.AddRange(childDescendants);
        }
        
        return descendants;
    }

    public async Task<Category> CreateAsync(Category category)
    {
        category.CreatedAt = DateTime.UtcNow;
        category.UpdatedAt = DateTime.UtcNow;
        
        _context.Categories.Add(category);
        await _context.SaveChangesAsync();
        
        return category;
    }

    public async Task UpdateAsync(Category category)
    {
        category.UpdatedAt = DateTime.UtcNow;
        
        _context.Categories.Update(category);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(long id)
    {
        var category = await _context.Categories.FindAsync(id);
        if (category != null)
        {
            _context.Categories.Remove(category);
            await _context.SaveChangesAsync();
        }
    }

    public async Task<bool> ExistsAsync(string name, long? excludeId = null)
    {
        var query = _context.Categories
            .Where(c => c.Name.ToLower() == name.ToLower());
        
        if (excludeId.HasValue)
        {
            query = query.Where(c => c.Id != excludeId.Value);
        }
        
        return await query.AnyAsync();
    }

    public async Task<bool> HasChildrenAsync(long categoryId)
    {
        return await _context.Categories
            .AnyAsync(c => c.ParentCategoryId == categoryId);
    }

    public async Task<bool> HasProductsAsync(long categoryId)
    {
        return await _context.Products
            .AnyAsync(p => p.CategoryId == categoryId);
    }

    public async Task<int> GetChildCountAsync(long parentCategoryId)
    {
        return await _context.Categories
            .CountAsync(c => c.ParentCategoryId == parentCategoryId);
    }

    public async Task<int> GetProductCountAsync(long categoryId)
    {
        return await _context.Products
            .CountAsync(p => p.CategoryId == categoryId);
    }
}
