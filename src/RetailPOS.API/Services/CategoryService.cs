using RetailPOS.API.DTOs.Category;
using RetailPOS.Infrastructure.Repositories;
using RetailPOS.Core.Entities;

namespace RetailPOS.API.Services;

/// <summary>
/// Service implementation for Category business logic
/// </summary>
public class CategoryService : ICategoryService
{
    private readonly ICategoryRepository _categoryRepository;
    private readonly ILogger<CategoryService> _logger;

    public CategoryService(
        ICategoryRepository categoryRepository,
        ILogger<CategoryService> logger)
    {
        _categoryRepository = categoryRepository;
        _logger = logger;
    }

    public async Task<CategoryDto?> GetCategoryByIdAsync(long id)
    {
        var category = await _categoryRepository.GetByIdAsync(id);
        if (category == null)
        {
            return null;
        }

        return await MapToDtoAsync(category);
    }

    public async Task<IEnumerable<CategoryDto>> GetAllCategoriesAsync()
    {
        var categories = await _categoryRepository.GetAllAsync();
        var dtos = new List<CategoryDto>();

        foreach (var category in categories)
        {
            dtos.Add(await MapToDtoAsync(category));
        }

        return dtos;
    }

    public async Task<IEnumerable<CategoryTreeDto>> GetCategoryTreeAsync()
    {
        // Get all root categories
        var rootCategories = await _categoryRepository.GetRootCategoriesAsync();
        var treeDtos = new List<CategoryTreeDto>();

        foreach (var root in rootCategories)
        {
            treeDtos.Add(await MapToTreeDtoAsync(root, 0));
        }

        return treeDtos;
    }

    public async Task<IEnumerable<CategoryDto>> GetChildrenAsync(long parentCategoryId)
    {
        var children = await _categoryRepository.GetChildrenAsync(parentCategoryId);
        var dtos = new List<CategoryDto>();

        foreach (var child in children)
        {
            dtos.Add(await MapToDtoAsync(child));
        }

        return dtos;
    }

    public async Task<CategoryDto> CreateCategoryAsync(CreateCategoryDto dto)
    {
        _logger.LogInformation("Creating new category: {CategoryName}", dto.Name);

        // Check if name already exists
        if (await _categoryRepository.ExistsAsync(dto.Name))
        {
            _logger.LogWarning("Category name already exists: {CategoryName}", dto.Name);
            throw new InvalidOperationException($"Category with name '{dto.Name}' already exists");
        }

        // Validate parent category if specified
        if (dto.ParentCategoryId.HasValue)
        {
            var parent = await _categoryRepository.GetByIdAsync(dto.ParentCategoryId.Value);
            if (parent == null)
            {
                _logger.LogWarning("Parent category not found: {ParentId}", dto.ParentCategoryId.Value);
                throw new KeyNotFoundException($"Parent category with ID {dto.ParentCategoryId.Value} not found");
            }
        }

        var category = new Category
        {
            Name = dto.Name,
            Description = dto.Description,
            ParentCategoryId = dto.ParentCategoryId,
            ImageUrl = dto.ImageUrl,
            DisplayOrder = dto.DisplayOrder,
            IsActive = dto.IsActive
        };

        var created = await _categoryRepository.CreateAsync(category);
        _logger.LogInformation("Category created successfully: {CategoryId}", created.Id);

        return await MapToDtoAsync(created);
    }

    public async Task<CategoryDto> UpdateCategoryAsync(long id, UpdateCategoryDto dto)
    {
        _logger.LogInformation("Updating category: {CategoryId}", id);

        var category = await _categoryRepository.GetByIdAsync(id);
        if (category == null)
        {
            _logger.LogWarning("Category not found: {CategoryId}", id);
            throw new KeyNotFoundException($"Category with ID {id} not found");
        }

        // Check if new name conflicts with existing category
        if (await _categoryRepository.ExistsAsync(dto.Name, id))
        {
            _logger.LogWarning("Category name already exists: {CategoryName}", dto.Name);
            throw new InvalidOperationException($"Category with name '{dto.Name}' already exists");
        }

        // Validate parent category if specified
        if (dto.ParentCategoryId.HasValue)
        {
            // Prevent category from being its own parent
            if (dto.ParentCategoryId.Value == id)
            {
                throw new InvalidOperationException("Category cannot be its own parent");
            }

            // Prevent circular references
            var descendants = await _categoryRepository.GetAllDescendantsAsync(id);
            if (descendants.Any(d => d.Id == dto.ParentCategoryId.Value))
            {
                throw new InvalidOperationException("Cannot set a descendant category as parent (circular reference)");
            }

            var parent = await _categoryRepository.GetByIdAsync(dto.ParentCategoryId.Value);
            if (parent == null)
            {
                _logger.LogWarning("Parent category not found: {ParentId}", dto.ParentCategoryId.Value);
                throw new KeyNotFoundException($"Parent category with ID {dto.ParentCategoryId.Value} not found");
            }
        }

        category.Name = dto.Name;
        category.Description = dto.Description;
        category.ParentCategoryId = dto.ParentCategoryId;
        category.ImageUrl = dto.ImageUrl;
        category.DisplayOrder = dto.DisplayOrder;
        category.IsActive = dto.IsActive;

        await _categoryRepository.UpdateAsync(category);
        _logger.LogInformation("Category updated successfully: {CategoryId}", id);

        return await MapToDtoAsync(category);
    }

    public async Task DeleteCategoryAsync(long id)
    {
        _logger.LogInformation("Deleting category: {CategoryId}", id);

        var category = await _categoryRepository.GetByIdAsync(id);
        if (category == null)
        {
            _logger.LogWarning("Category not found: {CategoryId}", id);
            throw new KeyNotFoundException($"Category with ID {id} not found");
        }

        // Check if category has children
        if (await _categoryRepository.HasChildrenAsync(id))
        {
            _logger.LogWarning("Cannot delete category with children: {CategoryId}", id);
            throw new InvalidOperationException("Cannot delete category that has child categories. Delete or move children first.");
        }

        // Check if category has products
        if (await _categoryRepository.HasProductsAsync(id))
        {
            _logger.LogWarning("Cannot delete category with products: {CategoryId}", id);
            throw new InvalidOperationException("Cannot delete category that has products. Reassign or delete products first.");
        }

        await _categoryRepository.DeleteAsync(id);
        _logger.LogInformation("Category deleted successfully: {CategoryId}", id);
    }

    public async Task<CategoryDto> MoveCategoryAsync(long id, MoveCategoryDto dto)
    {
        _logger.LogInformation("Moving category {CategoryId} to parent {ParentId}", id, dto.NewParentCategoryId);

        var category = await _categoryRepository.GetByIdAsync(id);
        if (category == null)
        {
            _logger.LogWarning("Category not found: {CategoryId}", id);
            throw new KeyNotFoundException($"Category with ID {id} not found");
        }

        // Validate new parent if specified
        if (dto.NewParentCategoryId.HasValue)
        {
            // Prevent category from being its own parent
            if (dto.NewParentCategoryId.Value == id)
            {
                throw new InvalidOperationException("Category cannot be its own parent");
            }

            // Prevent circular references
            var descendants = await _categoryRepository.GetAllDescendantsAsync(id);
            if (descendants.Any(d => d.Id == dto.NewParentCategoryId.Value))
            {
                throw new InvalidOperationException("Cannot move category to one of its descendants (circular reference)");
            }

            var parent = await _categoryRepository.GetByIdAsync(dto.NewParentCategoryId.Value);
            if (parent == null)
            {
                _logger.LogWarning("Parent category not found: {ParentId}", dto.NewParentCategoryId.Value);
                throw new KeyNotFoundException($"Parent category with ID {dto.NewParentCategoryId.Value} not found");
            }
        }

        category.ParentCategoryId = dto.NewParentCategoryId;
        
        if (dto.NewDisplayOrder.HasValue)
        {
            category.DisplayOrder = dto.NewDisplayOrder.Value;
        }

        await _categoryRepository.UpdateAsync(category);
        _logger.LogInformation("Category moved successfully: {CategoryId}", id);

        return await MapToDtoAsync(category);
    }

    #region Private Helper Methods

    /// <summary>
    /// Map Category entity to CategoryDto
    /// </summary>
    private async Task<CategoryDto> MapToDtoAsync(Category category)
    {
        var childCount = await _categoryRepository.GetChildCountAsync(category.Id);
        var productCount = await _categoryRepository.GetProductCountAsync(category.Id);

        return new CategoryDto
        {
            Id = category.Id,
            Name = category.Name,
            Description = category.Description,
            ParentCategoryId = category.ParentCategoryId,
            ParentCategoryName = category.ParentCategory?.Name,
            ImageUrl = category.ImageUrl,
            DisplayOrder = category.DisplayOrder,
            IsActive = category.IsActive,
            CreatedAt = category.CreatedAt,
            UpdatedAt = category.UpdatedAt,
            Level = await CalculateLevelAsync(category),
            ChildCount = childCount,
            ProductCount = productCount
        };
    }

    /// <summary>
    /// Map Category entity to CategoryTreeDto (recursive)
    /// </summary>
    private async Task<CategoryTreeDto> MapToTreeDtoAsync(Category category, int level)
    {
        var productCount = await _categoryRepository.GetProductCountAsync(category.Id);
        var children = await _categoryRepository.GetChildrenAsync(category.Id);

        var treeDto = new CategoryTreeDto
        {
            Id = category.Id,
            Name = category.Name,
            Description = category.Description,
            ParentCategoryId = category.ParentCategoryId,
            ImageUrl = category.ImageUrl,
            DisplayOrder = category.DisplayOrder,
            IsActive = category.IsActive,
            Level = level,
            ProductCount = productCount,
            Children = new List<CategoryTreeDto>()
        };

        // Recursively load children
        foreach (var child in children)
        {
            treeDto.Children.Add(await MapToTreeDtoAsync(child, level + 1));
        }

        return treeDto;
    }

    /// <summary>
    /// Calculate category level (depth in tree)
    /// </summary>
    private async Task<int> CalculateLevelAsync(Category category)
    {
        int level = 0;
        var current = category;

        while (current.ParentCategoryId.HasValue)
        {
            level++;
            current = await _categoryRepository.GetByIdAsync(current.ParentCategoryId.Value);
            if (current == null) break;
        }

        return level;
    }

    #endregion
}
