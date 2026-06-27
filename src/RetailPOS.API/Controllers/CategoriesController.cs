using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.DTOs.Category;
using RetailPOS.API.Services;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "categories.view")]
public class CategoriesController : ControllerBase
{
    private readonly ICategoryService _categoryService;
    private readonly ILogger<CategoriesController> _logger;

    public CategoriesController(
        ICategoryService categoryService,
        ILogger<CategoriesController> logger)
    {
        _categoryService = categoryService;
        _logger = logger;
    }

    /// <summary>
    /// Get all categories as flat list
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<CategoryDto>>> GetAllCategories()
    {
        try
        {
            var categories = await _categoryService.GetAllCategoriesAsync();
            return Ok(new
            {
                data = categories,
                count = categories.Count(),
                message = "Categories retrieved successfully"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving categories");
            return StatusCode(500, new { error = "An error occurred while retrieving categories" });
        }
    }

    /// <summary>
    /// Get all categories as hierarchical tree structure
    /// </summary>
    [HttpGet("tree")]
    public async Task<ActionResult<IEnumerable<CategoryTreeDto>>> GetCategoryTree()
    {
        try
        {
            var tree = await _categoryService.GetCategoryTreeAsync();
            return Ok(new
            {
                data = tree,
                count = tree.Count(),
                message = "Category tree retrieved successfully"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving category tree");
            return StatusCode(500, new { error = "An error occurred while retrieving category tree" });
        }
    }

    /// <summary>
    /// Get category by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<CategoryDto>> GetCategoryById(long id)
    {
        try
        {
            var category = await _categoryService.GetCategoryByIdAsync(id);
            if (category == null)
            {
                return NotFound(new { error = $"Category with ID {id} not found" });
            }

            return Ok(new { data = category, message = "Category retrieved successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving category {CategoryId}", id);
            return StatusCode(500, new { error = "An error occurred while retrieving category" });
        }
    }

    /// <summary>
    /// Get children of a specific category
    /// </summary>
    [HttpGet("{id}/children")]
    public async Task<ActionResult<IEnumerable<CategoryDto>>> GetCategoryChildren(long id)
    {
        try
        {
            var children = await _categoryService.GetChildrenAsync(id);
            return Ok(new
            {
                data = children,
                count = children.Count(),
                message = "Category children retrieved successfully"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving children for category {CategoryId}", id);
            return StatusCode(500, new { error = "An error occurred while retrieving category children" });
        }
    }

    /// <summary>
    /// Create new category
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "categories.create")]
    public async Task<ActionResult<CategoryDto>> CreateCategory([FromBody] CreateCategoryDto dto)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new { error = "Invalid data", details = ModelState });
            }

            var category = await _categoryService.CreateCategoryAsync(dto);
            _logger.LogInformation("Category created: {CategoryId} by user {UserId}",
                category.Id, User.Identity?.Name);

            return CreatedAtAction(
                nameof(GetCategoryById),
                new { id = category.Id },
                new { data = category, message = "Category created successfully" }
            );
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Invalid operation while creating category");
            return BadRequest(new { error = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "Parent category not found");
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating category");
            return StatusCode(500, new { error = "An error occurred while creating category" });
        }
    }

    /// <summary>
    /// Update existing category
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Policy = "categories.edit")]
    public async Task<ActionResult<CategoryDto>> UpdateCategory(long id, [FromBody] UpdateCategoryDto dto)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new { error = "Invalid data", details = ModelState });
            }

            var category = await _categoryService.UpdateCategoryAsync(id, dto);
            _logger.LogInformation("Category updated: {CategoryId} by user {UserId}",
                id, User.Identity?.Name);

            return Ok(new { data = category, message = "Category updated successfully" });
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "Category not found: {CategoryId}", id);
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Invalid operation while updating category");
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating category {CategoryId}", id);
            return StatusCode(500, new { error = "An error occurred while updating category" });
        }
    }

    /// <summary>
    /// Delete category
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Policy = "categories.delete")]
    public async Task<ActionResult> DeleteCategory(long id)
    {
        try
        {
            await _categoryService.DeleteCategoryAsync(id);
            _logger.LogInformation("Category deleted: {CategoryId} by user {UserId}",
                id, User.Identity?.Name);

            return Ok(new { message = "Category deleted successfully" });
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "Category not found: {CategoryId}", id);
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Invalid operation while deleting category");
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting category {CategoryId}", id);
            return StatusCode(500, new { error = "An error occurred while deleting category" });
        }
    }

    /// <summary>
    /// Move category to a different parent
    /// </summary>
    [HttpPut("{id}/move")]
    [Authorize(Policy = "categories.edit")]
    public async Task<ActionResult<CategoryDto>> MoveCategory(long id, [FromBody] MoveCategoryDto dto)
    {
        try
        {
            var category = await _categoryService.MoveCategoryAsync(id, dto);
            _logger.LogInformation("Category moved: {CategoryId} to parent {ParentId} by user {UserId}",
                id, dto.NewParentCategoryId, User.Identity?.Name);

            return Ok(new { data = category, message = "Category moved successfully" });
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "Category or parent not found");
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Invalid operation while moving category");
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error moving category {CategoryId}", id);
            return StatusCode(500, new { error = "An error occurred while moving category" });
        }
    }
}
