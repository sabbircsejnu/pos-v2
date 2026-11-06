using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.Authorization;
using MediatR;
using RetailPOS.API.DTOs.Product;
using RetailPOS.API.Models;
using RetailPOS.Application.Products.Commands;
using RetailPOS.Application.Products.Queries;

namespace RetailPOS.API.Controllers;

// Authenticated by default; each action declares its policy so editing a product implies reading that record.
[Authorize]
[ApiController]
[Route("api/products/{productId}/variations")]
public class ProductVariationsController : ControllerBase
{
    private readonly ISender _sender;
    private readonly ILogger<ProductVariationsController> _logger;

    public ProductVariationsController(
        ISender sender,
        ILogger<ProductVariationsController> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    /// <summary>
    /// Get variations assigned to a product
    /// </summary>
    [HttpGet]
    [Authorize(Policy = LookupAuthorizationPolicies.ProductRecordRead)]
    public async Task<ActionResult<ApiResponse<List<ProductVariationDto>>>> GetProductVariations(
        long productId,
        CancellationToken cancellationToken)
    {
        var readModels = await _sender.Send(
            new GetProductVariationsQuery(productId),
            cancellationToken);
        var variations = readModels
            .Select(variation => new ProductVariationDto
            {
                VariationId = variation.VariationId,
                VariationName = variation.VariationName,
                IsRequired = variation.IsRequired,
                Options = variation.Options
                    .Select(option => new VariationOptionInfo
                    {
                        Id = option.Id,
                        Name = option.Name,
                        PriceAdjustment = option.PriceAdjustment
                    })
                    .ToList(),
                SelectedOptionIds = variation.SelectedOptionIds.ToList()
            })
            .ToList();
        return Ok(ApiResponse<List<ProductVariationDto>>.SuccessResponse(variations));
    }

    /// <summary>
    /// Assign variations to a product (with per-variation selected options)
    /// </summary>
    [HttpPost("assign")]
    [Authorize(Policy = "products.edit")]
    public async Task<ActionResult<ApiResponse>> AssignVariations(
        long productId,
        [FromBody] AssignVariationsRequest request,
        CancellationToken cancellationToken)
    {
        await _sender.Send(
            new AssignProductVariationsCommand(
                productId,
                request.VariationIds,
                request.SelectedOptionsByVariation),
            cancellationToken);
        _logger.LogInformation(
            "Assigned {Count} variations to product {ProductId} with {OptionCount} total selected options",
            request.VariationIds.Count,
            productId,
            request.SelectedOptionsByVariation.Values.Sum(options => options.Count));
        return Ok(ApiResponse.SuccessResponse("Variations assigned successfully"));
    }

    /// <summary>
    /// Get all combinations for a product
    /// </summary>
    [HttpGet("combinations")]
    [Authorize(Policy = LookupAuthorizationPolicies.ProductRecordRead)]
    public async Task<ActionResult<ApiResponse<List<CombinationDto>>>> GetCombinations(
        long productId,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new GetProductCombinationsQuery(productId),
            cancellationToken);
        var combinations = MapCombinations(result);
        return Ok(ApiResponse<List<CombinationDto>>.SuccessResponse(combinations));
    }

    /// <summary>
    /// Generate all possible combinations
    /// </summary>
    [HttpPost("combinations/generate-all")]
    [Authorize(Policy = "products.edit")]
    public async Task<ActionResult<ApiResponse<List<CombinationDto>>>> GenerateAllCombinations(
        long productId,
        CancellationToken cancellationToken)
    {
        var generated = await _sender.Send(
            new GenerateProductCombinationsCommand(productId),
            cancellationToken);
        var combinations = MapCombinations(generated);
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
        [FromBody] GenerateCombinationsRequest request,
        CancellationToken cancellationToken)
    {
        var generated = await _sender.Send(
            new GenerateSelectedProductCombinationsCommand(
                productId,
                request.VariationIds ?? []),
            cancellationToken);
        var combinations = MapCombinations(generated);
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
        [FromBody] CreateCombinationRequest request,
        CancellationToken cancellationToken)
    {
        var combination = await _sender.Send(
            new CreateManualProductCombinationCommand(
                productId,
                request.OptionIds,
                request.Sku,
                request.Barcode,
                request.PriceAdjustment,
                request.CostAdjustment),
            cancellationToken);
        return CreatedAtAction(
            nameof(GetCombinations), 
            new { productId }, 
            ApiResponse<CombinationDto>.SuccessResponse(
                MapCombination(combination),
                "Combination created successfully"));
    }

    /// <summary>
    /// Update a combination
    /// </summary>
    [HttpPut("combinations/{variantId}")]
    [Authorize(Policy = "products.edit")]
    public async Task<ActionResult<ApiResponse>> UpdateCombination(
        long productId,
        long variantId,
        [FromBody] UpdateCombinationRequest request,
        CancellationToken cancellationToken)
    {
        await _sender.Send(
            new UpdateProductVariantCommand(
                variantId,
                request.Sku,
                request.Barcode,
                request.PriceAdjustment,
                request.CostAdjustment),
            cancellationToken);
        return Ok(ApiResponse.SuccessResponse("Combination updated successfully"));
    }

    /// <summary>
    /// Delete a combination
    /// </summary>
    [HttpDelete("combinations/{variantId}")]
    [Authorize(Policy = "products.edit")]
    public async Task<ActionResult<ApiResponse>> DeleteCombination(
        long productId,
        long variantId,
        CancellationToken cancellationToken)
    {
        await _sender.Send(new DeleteProductVariantCommand(variantId), cancellationToken);
        return Ok(ApiResponse.SuccessResponse("Combination deleted successfully"));
    }

    private static List<CombinationDto> MapCombinations(
        IReadOnlyList<RetailPOS.Application.Products.ReadModels.ProductCombinationModel> generated) =>
        generated.Select(MapCombination).ToList();

    private static CombinationDto MapCombination(
        RetailPOS.Application.Products.ReadModels.ProductCombinationModel combination) =>
        new()
        {
            Id = combination.Id,
            Sku = combination.Sku,
            Barcode = combination.Barcode,
            PriceAdjustment = combination.PriceAdjustment,
            CostAdjustment = combination.CostAdjustment,
            OptionIds = combination.OptionIds.ToList(),
            Options = combination.Options.Select(option => new CombinationOptionInfo
            {
                Id = option.OptionId,
                VariationName = option.VariationName,
                OptionName = option.OptionName,
                PriceAdjustment = option.PriceAdjustment
            }).ToList(),
            CombinationName = combination.CombinationName,
            FinalPrice = combination.FinalPrice
        };
}
