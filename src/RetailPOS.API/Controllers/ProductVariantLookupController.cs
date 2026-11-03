using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.Authorization;
using RetailPOS.API.DTOs.Product;
using RetailPOS.API.Services;
using RetailPOS.Application.Authorization;
using RetailPOS.Application.Products.Queries;
using RetailPOS.Application.Products.ReadModels;

namespace RetailPOS.API.Controllers;

/// <summary>
/// Read-only product variant lookup embedded in several screens (inventory filters, stock transfers and
/// adjustments, purchase orders, inventory reports). It is authorised by
/// <see cref="LookupAuthorizationPolicies.ProductVariantSearch"/> rather than the full products.view
/// permission; Business and Outlet/Warehouse scope and cost redaction are enforced exactly as before.
/// </summary>
[ApiController]
[Route("api/products/variants")]
[Authorize(Policy = LookupAuthorizationPolicies.ProductVariantSearch)]
public class ProductVariantLookupController : ControllerBase
{
    private readonly ILogger<ProductVariantLookupController> _logger;
    private readonly ISender _sender;
    private readonly IVariantDisplayFormatterService _variantDisplayFormatter;
    private readonly IUserOutletAccessService _outletAccess;

    public ProductVariantLookupController(
        ILogger<ProductVariantLookupController> logger,
        ISender sender,
        IVariantDisplayFormatterService variantDisplayFormatter,
        IUserOutletAccessService outletAccess)
    {
        _logger = logger;
        _sender = sender;
        _variantDisplayFormatter = variantDisplayFormatter;
        _outletAccess = outletAccess;
    }

    /// <summary>
    /// Search product variants for PO/GRN forms
    /// </summary>
    [HttpGet("search")]
    public async Task<ActionResult> SearchVariants(
        [FromQuery] string query = "",
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] long? locationId = null,
        [FromQuery] string? locationType = null,
        CancellationToken cancellationToken = default)
    {
        (long? LocationId, string? LocationType) authorizedLocation;
        try
        {
            authorizedLocation = await StockLocationRequestAuthorizer.ResolveAndAuthorizeAsync(
                _outletAccess,
                locationId,
                locationType,
                Request.Query.ContainsKey("locationType"));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = ex.Message });
        }

        try
        {
            var results = await _sender.Send(
                new SearchProductVariantsQuery(new ProductVariantSearchCriteria(
                    query,
                    pageNumber,
                    pageSize,
                    authorizedLocation.LocationId,
                    authorizedLocation.LocationType)),
                cancellationToken);
            var variants = new List<ProductVariantSearchDto>(results.Count);
            foreach (var result in results)
            {
                variants.Add(new ProductVariantSearchDto
                {
                    Id = result.Id,
                    ProductId = result.ProductId,
                    ProductName = result.ProductName,
                    ProductCode = result.ProductCode,
                    Name = result.Name,
                    Sku = result.Sku,
                    Barcode = result.Barcode,
                    Attributes = result.Attributes,
                    VariantDisplay = await _variantDisplayFormatter.FormatAsync(
                        result.Attributes,
                        cancellationToken),
                    FinalPrice = result.FinalPrice,
                    CostPrice = result.CostPrice,
                    StockQuantity = result.StockQuantity,
                    PrimaryImageThumb = result.PrimaryImageThumb
                });
            }

            if (!User.CanViewCost())
            {
                foreach (var variant in variants) variant.CostPrice = null;
            }

            return Ok(new { data = variants, message = "Variants retrieved successfully" });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (BusinessContextRequiredException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching variants");
            return StatusCode(500, new { error = "Failed to search variants" });
        }
    }
}
