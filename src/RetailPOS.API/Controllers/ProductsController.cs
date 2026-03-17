using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.DTOs.Product;
using RetailPOS.API.Services;

namespace RetailPOS.API.Controllers;

/// <summary>
/// Controller for Product management operations
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ProductsController : ControllerBase
{
    private readonly IProductService _productService;
    private readonly ILogger<ProductsController> _logger;

    public ProductsController(IProductService productService, ILogger<ProductsController> logger)
    {
        _productService = productService;
        _logger = logger;
    }

    /// <summary>
    /// Get all products
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ProductDto>>> GetAll()
    {
        try
        {
            var products = await _productService.GetAllAsync();
            return Ok(new { data = products, message = "Products retrieved successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving products");
            return StatusCode(500, new { error = "Failed to retrieve products" });
        }
    }

    /// <summary>
    /// Search products with filters and pagination
    /// </summary>
    [HttpPost("search")]
    public async Task<ActionResult<ProductListDto>> Search([FromBody] ProductSearchDto searchDto)
    {
        try
        {
            var result = await _productService.SearchAsync(searchDto);
            return Ok(new { data = result, message = "Products searched successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching products");
            return StatusCode(500, new { error = "Failed to search products" });
        }
    }

    /// <summary>
    /// Get product by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ProductDto>> GetById(long id)
    {
        try
        {
            var product = await _productService.GetByIdAsync(id);
            if (product == null)
            {
                return NotFound(new { error = $"Product with ID {id} not found" });
            }

            return Ok(new { data = product, message = "Product retrieved successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving product {ProductId}", id);
            return StatusCode(500, new { error = "Failed to retrieve product" });
        }
    }

    /// <summary>
    /// Get product by SKU
    /// </summary>
    [HttpGet("sku/{sku}")]
    public async Task<ActionResult<ProductDto>> GetBySku(string sku)
    {
        try
        {
            var product = await _productService.GetBySkuAsync(sku);
            if (product == null)
            {
                return NotFound(new { error = $"Product with SKU '{sku}' not found" });
            }

            return Ok(new { data = product, message = "Product retrieved successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving product by SKU {Sku}", sku);
            return StatusCode(500, new { error = "Failed to retrieve product" });
        }
    }

    /// <summary>
    /// Get product by Barcode
    /// </summary>
    [HttpGet("barcode/{barcode}")]
    public async Task<ActionResult<ProductDto>> GetByBarcode(string barcode)
    {
        try
        {
            var product = await _productService.GetByBarcodeAsync(barcode);
            if (product == null)
            {
                return NotFound(new { error = $"Product with barcode '{barcode}' not found" });
            }

            return Ok(new { data = product, message = "Product retrieved successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving product by barcode {Barcode}", barcode);
            return StatusCode(500, new { error = "Failed to retrieve product" });
        }
    }

    /// <summary>
    /// Get products by category
    /// </summary>
    [HttpGet("category/{categoryId}")]
    public async Task<ActionResult<IEnumerable<ProductDto>>> GetByCategory(long categoryId)
    {
        try
        {
            var products = await _productService.GetByCategoryAsync(categoryId);
            return Ok(new { data = products, message = "Products retrieved successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving products for category {CategoryId}", categoryId);
            return StatusCode(500, new { error = "Failed to retrieve products" });
        }
    }

    /// <summary>
    /// Create a new product
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<ProductDto>> Create([FromBody] CreateProductDto createDto)
    {
        try
        {
            var product = await _productService.CreateAsync(createDto);
            return CreatedAtAction(nameof(GetById), new { id = product.Id }, new { data = product, message = "Product created successfully" });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Invalid operation while creating product");
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating product");
            return StatusCode(500, new { error = "Failed to create product" });
        }
    }

    /// <summary>
    /// Update an existing product
    /// </summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<ProductDto>> Update(long id, [FromBody] UpdateProductDto updateDto)
    {
        try
        {
            var product = await _productService.UpdateAsync(id, updateDto);
            return Ok(new { data = product, message = "Product updated successfully" });
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "Product not found");
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Invalid operation while updating product");
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating product {ProductId}", id);
            return StatusCode(500, new { error = "Failed to update product" });
        }
    }

    /// <summary>
    /// Delete a product
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<ActionResult> Delete(long id)
    {
        try
        {
            await _productService.DeleteAsync(id);
            return Ok(new { message = "Product deleted successfully" });
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "Product not found");
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Cannot delete product");
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting product {ProductId}", id);
            return StatusCode(500, new { error = "Failed to delete product" });
        }
    }

    /// <summary>
    /// Generate SKU for a product name
    /// </summary>
    [HttpPost("generate-sku")]
    public async Task<ActionResult<string>> GenerateSku([FromBody] string productName)
    {
        try
        {
            var sku = await _productService.GenerateSkuAsync(productName);
            return Ok(new { data = sku, message = "SKU generated successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating SKU");
            return StatusCode(500, new { error = "Failed to generate SKU" });
        }
    }

    /// <summary>
    /// Search product variants for PO/GRN forms
    /// </summary>
    [HttpGet("variants/search")]
    public async Task<ActionResult> SearchVariants([FromQuery] string query = "", [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20)
    {
        try
        {
            var variants = await _productService.SearchVariantsAsync(query, pageNumber, pageSize);
            return Ok(new { data = variants, message = "Variants retrieved successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching variants");
            return StatusCode(500, new { error = "Failed to search variants" });
        }
    }
}
