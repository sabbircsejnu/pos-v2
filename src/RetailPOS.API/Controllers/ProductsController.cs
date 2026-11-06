using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.Authorization;
using RetailPOS.API.DTOs.Product;
using RetailPOS.API.Services;
using RetailPOS.Application.Products.Commands;
using RetailPOS.Application.Products.Queries;
using RetailPOS.Application.Products.ReadModels;
using MediatR;

namespace RetailPOS.API.Controllers;

/// <summary>
/// Controller for Product management operations
/// </summary>
[ApiController]
[Route("api/[controller]")]
// Authenticated by default; each action declares its policy so editing a product implies reading that record.
[Authorize]
public class ProductsController : ControllerBase
{
    private readonly ILogger<ProductsController> _logger;
    private readonly ISender _sender;

    public ProductsController(
        ILogger<ProductsController> logger,
        ISender sender)
    {
        _logger = logger;
        _sender = sender;
    }

    private static ProductDto ToDto(ProductReadModel product) => new()
    {
        Id = product.Id,
        Name = product.Name,
        Description = product.Description,
        ProductCode = product.ProductCode,
        Sku = product.Sku,
        Barcode = product.Barcode,
        CategoryId = product.CategoryId,
        CategoryName = product.CategoryName,
        BasePrice = product.BasePrice,
        CostPrice = product.CostPrice,
        TaxRate = product.TaxRate,
        TaxClassification = product.TaxClassification,
        HasVariants = product.HasVariants,
        PrimaryImageThumb = product.PrimaryImageThumb,
        PrimaryImageMedium = product.PrimaryImageMedium,
        Status = product.Status,
        IsActive = product.IsActive,
        VariantCount = product.VariantCount,
        TotalStock = product.TotalStock,
        StockStatus = product.StockStatus,
        Variants = product.Variants.Select(variant => new ProductVariantDto
        {
            Id = variant.Id,
            ProductId = variant.ProductId,
            ProductCode = variant.ProductCode,
            Name = variant.Name,
            Sku = variant.Sku,
            Barcode = variant.Barcode,
            Attributes = variant.Attributes,
            PriceAdjustment = variant.PriceAdjustment,
            FinalPrice = variant.FinalPrice,
            CreatedAt = variant.CreatedAt,
            UpdatedAt = variant.UpdatedAt
        }).ToList(),
        CreatedAt = product.CreatedAt,
        UpdatedAt = product.UpdatedAt
    };

    // ── Cost-visibility helpers ──────────────────────────────────────────────
    /// <summary>Strips cost fields from <paramref name="dto"/> when the caller lacks products.view_cost.</summary>
    private void RedactCost(ProductDto dto)
    {
        if (User.CanViewCost()) return;
        dto.CostPrice = null;
    }

    private void RedactCost(IEnumerable<ProductDto> products)
    {
        if (User.CanViewCost()) return;
        foreach (var p in products) RedactCost(p);
    }

    private void RedactCost(ProductListDto list)
    {
        if (User.CanViewCost()) return;
        foreach (var p in list.Products) RedactCost(p);
    }
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Get all products
    /// </summary>
    [HttpGet]
    [Authorize(Policy = "products.view")]
    public async Task<ActionResult<IEnumerable<ProductDto>>> GetAll(CancellationToken cancellationToken)
    {
        try
        {
            var products = (await _sender.Send(new GetAllProductsQuery(), cancellationToken))
                .Select(ToDto)
                .ToArray();
            RedactCost(products);
            return Ok(new { data = products, message = "Products retrieved successfully" });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
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
    [Authorize(Policy = "products.view")]
    public async Task<ActionResult<ProductListDto>> Search([FromBody] ProductSearchDto searchDto)
    {
        try
        {
            var result = await _sender.Send(
                new SearchProductsQuery(new ProductSearchCriteria(
                    searchDto.SearchQuery,
                    searchDto.CategoryId,
                    searchDto.Status,
                    searchDto.HasVariants,
                    searchDto.MinPrice,
                    searchDto.MaxPrice,
                    searchDto.PageNumber,
                    searchDto.PageSize,
                    searchDto.SortBy,
                    searchDto.SortOrder)
                {
                    OutletId = searchDto.OutletId,
                    WarehouseId = searchDto.WarehouseId
                }),
                HttpContext.RequestAborted);
            var response = new ProductListDto
            {
                Products = result.Products.Select(ToDto).ToList(),
                TotalCount = result.TotalCount,
                PageNumber = result.PageNumber,
                PageSize = result.PageSize
            };
            RedactCost(response);
            return Ok(new { data = response, message = "Products searched successfully" });
        }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Product stock location access denied");
            return StatusCode(403, new { error = "The requested inventory location is not authorized." });
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid product stock location");
            return BadRequest(new { error = "Select either a valid outlet or a valid warehouse." });
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
    [Authorize(Policy = LookupAuthorizationPolicies.ProductRecordRead)]
    public async Task<ActionResult<ProductDto>> GetById(
        long id, CancellationToken cancellationToken,
        [FromQuery] long? outletId = null, [FromQuery] long? warehouseId = null)
    {
        try
        {
            var product = await _sender.Send(new GetProductByIdQuery(id, outletId, warehouseId), cancellationToken);
            if (product == null)
            {
                return NotFound(new { error = $"Product with ID {id} not found" });
            }

            var response = ToDto(product);
            RedactCost(response);
            return Ok(new { data = response, message = "Product retrieved successfully" });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Product stock location access denied");
            return StatusCode(403, new { error = "The requested inventory location is not authorized." });
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid product stock location");
            return BadRequest(new { error = "Select either a valid outlet or a valid warehouse." });
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
    [Authorize(Policy = "products.view")]
    public async Task<ActionResult<ProductDto>> GetBySku(string sku, CancellationToken cancellationToken)
    {
        try
        {
            var product = await _sender.Send(new GetProductBySkuQuery(sku), cancellationToken);
            if (product == null)
            {
                return NotFound(new { error = $"Product with SKU '{sku}' not found" });
            }

            var response = ToDto(product);
            RedactCost(response);
            return Ok(new { data = response, message = "Product retrieved successfully" });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
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
    [Authorize(Policy = "products.view")]
    public async Task<ActionResult<ProductDto>> GetByBarcode(string barcode, CancellationToken cancellationToken)
    {
        try
        {
            var product = await _sender.Send(new GetProductByBarcodeQuery(barcode), cancellationToken);
            if (product == null)
            {
                return NotFound(new { error = $"Product with barcode '{barcode}' not found" });
            }

            var response = ToDto(product);
            RedactCost(response);
            return Ok(new { data = response, message = "Product retrieved successfully" });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
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
    [Authorize(Policy = "products.view")]
    public async Task<ActionResult<IEnumerable<ProductDto>>> GetByCategory(
        long categoryId,
        CancellationToken cancellationToken)
    {
        try
        {
            var products = (await _sender.Send(
                    new GetProductsByCategoryQuery(categoryId),
                    cancellationToken))
                .Select(ToDto)
                .ToArray();
            RedactCost(products);
            return Ok(new { data = products, message = "Products retrieved successfully" });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
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
    [Authorize(Policy = "products.create")]
    public async Task<ActionResult<ProductDto>> Create(
        [FromBody] CreateProductDto createDto,
        CancellationToken cancellationToken)
    {
        try
        {
            var product = ToDto(await _sender.Send(new CreateProductCommand(
                createDto.Name,
                createDto.Description,
                createDto.ProductCode,
                createDto.Sku,
                createDto.Barcode,
                createDto.CategoryId,
                createDto.BasePrice,
                createDto.CostPrice,
                createDto.TaxRate,
                createDto.TaxClassification,
                createDto.Status,
                createDto.HasVariants,
                createDto.Variants?.Select(variant => new ProductVariantCreation(
                    variant.Name,
                    variant.Sku,
                    variant.Barcode,
                    variant.Attributes,
                    variant.PriceAdjustment)).ToArray() ?? []), cancellationToken));
            RedactCost(product);
            return CreatedAtAction(nameof(GetById), new { id = product.Id }, new { data = product, message = "Product created successfully" });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
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
    [Authorize(Policy = "products.edit")]
    public async Task<ActionResult<ProductDto>> Update(
        long id,
        [FromBody] UpdateProductDto updateDto,
        CancellationToken cancellationToken)
    {
        try
        {
            var product = ToDto(await _sender.Send(new UpdateProductCommand(
                id,
                updateDto.Name,
                updateDto.Description,
                updateDto.ProductCode,
                updateDto.Sku,
                updateDto.Barcode,
                updateDto.CategoryId,
                updateDto.BasePrice,
                updateDto.CostPrice,
                updateDto.TaxRate,
                updateDto.TaxClassification,
                updateDto.Status), cancellationToken));
            RedactCost(product);
            return Ok(new { data = product, message = "Product updated successfully" });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
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
    [Authorize(Policy = "products.view")]
    [Authorize(Policy = "products.delete")]
    public async Task<ActionResult> Delete(long id, CancellationToken cancellationToken)
    {
        try
        {
            await _sender.Send(new DeleteProductCommand(id), cancellationToken);
            return Ok(new { message = "Product deleted successfully" });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
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
    [Authorize(Policy = LookupAuthorizationPolicies.ProductCodeGeneration)]
    public async Task<ActionResult<string>> GenerateSku(
        [FromBody] string productName,
        CancellationToken cancellationToken)
    {
        try
        {
            var sku = await _sender.Send(new GenerateProductSkuCommand(productName), cancellationToken);
            return Ok(new { data = sku, message = "SKU generated successfully" });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating SKU");
            return StatusCode(500, new { error = "Failed to generate SKU" });
        }
    }

    /// <summary>
    /// Generate a unique EAN-13 barcode for a product
    /// </summary>
    [HttpPost("generate-barcode")]
    [Authorize(Policy = LookupAuthorizationPolicies.ProductCodeGeneration)]
    public async Task<ActionResult<string>> GenerateBarcode(
        [FromQuery] long? categoryId,
        CancellationToken cancellationToken)
    {
        try
        {
            var barcode = await _sender.Send(
                new GenerateProductBarcodeCommand(categoryId),
                cancellationToken);
            return Ok(new { data = barcode, message = "Barcode generated successfully" });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Unable to generate a unique product barcode");
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating product barcode");
            return StatusCode(500, new { error = "Failed to generate barcode" });
        }
    }

    // Product variant search (api/products/variants/search) lives in ProductVariantLookupController so
    // every screen that embeds the variant picker can use it without the full products.view permission.
}
