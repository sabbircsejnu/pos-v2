using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.DTOs.Product;
using RetailPOS.API.Models;
using RetailPOS.API.Services;

namespace RetailPOS.API.Controllers;

[Authorize(Policy = "products.view")]
[ApiController]
[Route("api/products/{productId}/variations")]
public class ProductVariationsController : ControllerBase
{
    private readonly IProductVariationService _service;
    private readonly ILogger<ProductVariationsController> _logger;

    public ProductVariationsController(
        IProductVariationService service,
        ILogger<ProductVariationsController> logger)
    {
        _service = service;
        _logger = logger;
    }

    /// <summary>
    /// Get variations assigned to a product
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<ProductVariationDto>>>> GetProductVariations(long productId)
    {
        var variations = await _service.GetProductVariationsAsync(productId);
        return Ok(ApiResponse<List<ProductVariationDto>>.SuccessResponse(variations));
    }

    /// <summary>
    /// Assign variations to a product (with per-variation selected options)
    /// </summary>
    [HttpPost("assign")]
    [Authorize(Policy = "products.edit")]
    public async Task<ActionResult<ApiResponse>> AssignVariations(long productId, [FromBody] AssignVariationsRequest request)
    {
        await _service.AssignVariationsToProductAsync(productId, request.VariationIds, request.SelectedOptionsByVariation);
        return Ok(ApiResponse.SuccessResponse("Variations assigned successfully"));
    }

    /// <summary>
    /// Get all combinations for a product
    /// </summary>
    [HttpGet("combinations")]
    public async Task<ActionResult<ApiResponse<List<CombinationDto>>>> GetCombinations(long productId)
    {
        var combinations = await _service.GetProductCombinationsAsync(productId);
        return Ok(ApiResponse<List<CombinationDto>>.SuccessResponse(combinations));
    }

    /// <summary>
    /// Generate all possible combinations
    /// </summary>
    [HttpPost("combinations/generate-all")]
    [Authorize(Policy = "products.edit")]
    public async Task<ActionResult<ApiResponse<List<CombinationDto>>>> GenerateAllCombinations(long productId)
    {
        var combinations = await _service.GenerateAllCombinationsAsync(productId);
        var message = combinations.Count > 0 
            ? $"Successfully generated {combinations.Count} new combination(s)" 
            : "All combinations already exist";
        return Ok(ApiResponse<List<CombinationDto>>.SuccessResponse(combinations, message));
    }

    /// <summary>
    /// Generate combinations for selected variations
    /// </summary>
    [HttpPost("combinations/generate-selected")]
    [Authorize(Policy = "products.edit")]
    public async Task<ActionResult<ApiResponse<List<CombinationDto>>>> GenerateSelectedCombinations(
        long productId,
        [FromBody] GenerateCombinationsRequest request)
    {
        var combinations = await _service.GenerateSelectedCombinationsAsync(productId, request.VariationIds ?? new List<long>());
        var message = combinations.Count > 0 
            ? $"Successfully generated {combinations.Count} new combination(s)" 
            : "All combinations already exist";
        return Ok(ApiResponse<List<CombinationDto>>.SuccessResponse(combinations, message));
    }

    /// <summary>
    /// Create a manual combination
    /// </summary>
    [HttpPost("combinations")]
    [Authorize(Policy = "products.edit")]
    public async Task<ActionResult<ApiResponse<CombinationDto>>> CreateManualCombination(
        long productId,
        [FromBody] CreateCombinationRequest request)
    {
        var combination = await _service.CreateManualCombinationAsync(productId, request);
        return CreatedAtAction(
            nameof(GetCombinations), 
            new { productId }, 
            ApiResponse<CombinationDto>.SuccessResponse(combination, "Combination created successfully"));
    }

    /// <summary>
    /// Update a combination
    /// </summary>
    [HttpPut("combinations/{variantId}")]
    [Authorize(Policy = "products.edit")]
    public async Task<ActionResult<ApiResponse>> UpdateCombination(
        long productId,
        long variantId,
        [FromBody] UpdateCombinationRequest request)
    {
        await _service.UpdateCombinationAsync(variantId, request);
        return Ok(ApiResponse.SuccessResponse("Combination updated successfully"));
    }

    /// <summary>
    /// Delete a combination
    /// </summary>
    [HttpDelete("combinations/{variantId}")]
    [Authorize(Policy = "products.edit")]
    public async Task<ActionResult<ApiResponse>> DeleteCombination(long productId, long variantId)
    {
        await _service.DeleteCombinationAsync(variantId);
        return Ok(ApiResponse.SuccessResponse("Combination deleted successfully"));
    }
}
