using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using RetailPOS.Application.Categories.Commands;
using RetailPOS.Application.Categories.Contracts;
using RetailPOS.Application.Categories.Queries;
using RetailPOS.API.DTOs.Category;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "categories.view")]
public class CategoriesController : ControllerBase
{
    private readonly ISender _sender;
    private readonly ILogger<CategoriesController> _logger;

    public CategoriesController(
        ISender sender,
        ILogger<CategoriesController> logger)
    {
        _sender = sender;
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
            var categories = (await _sender.Send(new GetCategoriesQuery(), HttpContext.RequestAborted))
                .Select(ToDto)
                .ToArray();
            return Ok(new
            {
                data = categories,
                count = categories.Length,
                message = "Categories retrieved successfully"
            });
        }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
        {
            throw;
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
    public async Task<ActionResult<IEnumerable<CategoryTreeDto>>> GetCategoryTree(
        CancellationToken cancellationToken)
    {
        try
        {
            var tree = (await _sender.Send(
                    new GetCategoryTreeQuery(),
                    cancellationToken))
                .Select(ToTreeDto)
                .ToArray();
            return Ok(new
            {
                data = tree,
                count = tree.Length,
                message = "Category tree retrieved successfully"
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
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
    public async Task<ActionResult<CategoryDto>> GetCategoryById(
        long id,
        CancellationToken cancellationToken)
    {
        try
        {
            var category = await _sender.Send(new GetCategoryByIdQuery(id), cancellationToken);
            if (category == null)
            {
                return NotFound(new { error = $"Category with ID {id} not found" });
            }

            return Ok(new { data = category, message = "Category retrieved successfully" });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
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
    public async Task<ActionResult<IEnumerable<CategoryDto>>> GetCategoryChildren(
        long id,
        CancellationToken cancellationToken)
    {
        try
        {
            var children = (await _sender.Send(
                    new GetCategoryChildrenQuery(id),
                    cancellationToken))
                .Select(ToDto)
                .ToArray();
            return Ok(new
            {
                data = children,
                count = children.Length,
                message = "Category children retrieved successfully"
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
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

            var result = await _sender.Send(new CreateCategoryCommand(
                dto.Name,
                dto.Description,
                dto.ParentCategoryId,
                dto.ImageUrl,
                dto.DisplayOrder,
                dto.IsActive), HttpContext.RequestAborted);
            var category = ToDto(result);
            _logger.LogInformation("Category created: {CategoryId} by user {UserId}",
                category.Id, User.Identity?.Name);

            return CreatedAtAction(
                nameof(GetCategoryById),
                new { id = category.Id },
                new { data = category, message = "Category created successfully" }
            );
        }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
        {
            throw;
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
        catch (FluentValidation.ValidationException ex)
        {
            var modelState = new Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary();
            foreach (var failure in ex.Errors)
            {
                modelState.AddModelError(failure.PropertyName, failure.ErrorMessage);
            }

            return ValidationProblem(modelState);
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
    public async Task<ActionResult<CategoryDto>> UpdateCategory(
        long id,
        [FromBody] UpdateCategoryDto dto,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new { error = "Invalid data", details = ModelState });
            }

            var category = ToDto(await _sender.Send(new UpdateCategoryCommand(
                id,
                dto.Name,
                dto.Description,
                dto.ParentCategoryId,
                dto.ImageUrl,
                dto.DisplayOrder,
                dto.IsActive), cancellationToken));
            _logger.LogInformation("Category updated: {CategoryId} by user {UserId}",
                id, User.Identity?.Name);

            return Ok(new { data = category, message = "Category updated successfully" });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
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
    public async Task<ActionResult> DeleteCategory(long id, CancellationToken cancellationToken)
    {
        try
        {
            await _sender.Send(new DeleteCategoryCommand(id), cancellationToken);
            _logger.LogInformation("Category deleted: {CategoryId} by user {UserId}",
                id, User.Identity?.Name);

            return Ok(new { message = "Category deleted successfully" });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
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
    public async Task<ActionResult<CategoryDto>> MoveCategory(
        long id,
        [FromBody] MoveCategoryDto dto,
        CancellationToken cancellationToken)
    {
        try
        {
            var category = ToDto(await _sender.Send(new MoveCategoryCommand(
                id,
                dto.NewParentCategoryId,
                dto.NewDisplayOrder), cancellationToken));
            _logger.LogInformation("Category moved: {CategoryId} to parent {ParentId} by user {UserId}",
                id, dto.NewParentCategoryId, User.Identity?.Name);

            return Ok(new { data = category, message = "Category moved successfully" });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
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

    private static CategoryDto ToDto(CategoryView category) => new()
    {
        Id = category.Id,
        Name = category.Name,
        Description = category.Description,
        ParentCategoryId = category.ParentCategoryId,
        ParentCategoryName = category.ParentCategoryName,
        ImageUrl = category.ImageUrl,
        DisplayOrder = category.DisplayOrder,
        IsActive = category.IsActive,
        CreatedAt = category.CreatedAt,
        UpdatedAt = category.UpdatedAt,
        Level = category.Level,
        ChildCount = category.ChildCount,
        ProductCount = category.ProductCount
    };

    private static CategoryTreeDto ToTreeDto(
        CategoryTreeReadModel category) => new()
    {
        Id = category.Id,
        Name = category.Name,
        Description = category.Description,
        ParentCategoryId = category.ParentCategoryId,
        ImageUrl = category.ImageUrl,
        DisplayOrder = category.DisplayOrder,
        IsActive = category.IsActive,
        Level = category.Level,
        ProductCount = category.ProductCount,
        Children = category.Children.Select(ToTreeDto).ToList()
    };
}
