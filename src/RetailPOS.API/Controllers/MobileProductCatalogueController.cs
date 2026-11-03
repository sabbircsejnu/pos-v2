using RetailPOS.Application.Mobile;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.Authorization;
using RetailPOS.API.DTOs.Mobile;
using RetailPOS.API.Models;
using RetailPOS.API.Services;
using RetailPOS.Application.Products.Abstractions;
using RetailPOS.Application.Products.ReadModels;
using RetailPOS.Core.Entities;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/mobile/v1/catalogue")]
[Authorize]
public sealed class MobileProductCatalogueController(
    IMobileProductCatalogueReadStore catalogue,
    IRoleSwitchContext roleSwitch,
    IMobileSingleBusinessReadinessGuard deploymentGuard,
    ILogger<MobileProductCatalogueController> logger) : ControllerBase
{
    private const int MaximumPageSize = 100;
    private const int MaximumPage = 100_000;

    [HttpGet("categories")]
    [ProducesResponseType(typeof(ApiResponse<MobileProductCategoryPageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<MobileProductCategoryPageDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<MobileProductCategoryPageDto>>> GetCategories(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100,
        CancellationToken cancellationToken = default)
    {
        if (!TryAuthorizeCatalogueRead(out var businessId))
            return Forbid();
        if (!IsValidPage(page, pageSize))
            return BadRequest(ApiResponse<MobileProductCategoryPageDto>.ErrorResponse("INVALID_PAGINATION"));
        if (!await deploymentGuard.IsReadyAsync(businessId, cancellationToken))
            return Forbid();

        var result = await catalogue.ListCategoriesAsync(page, pageSize, cancellationToken);
        return Ok(ApiResponse<MobileProductCategoryPageDto>.SuccessResponse(new MobileProductCategoryPageDto(
            result.Items.Select(category => new MobileProductCategoryDto(
                category.Id,
                category.Name,
                category.ParentCategoryId)).ToArray(),
            result.Page,
            result.PageSize,
            result.TotalCount)));
    }

    [HttpGet("products")]
    [ProducesResponseType(typeof(ApiResponse<MobileProductCataloguePageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<MobileProductCataloguePageDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<MobileProductCataloguePageDto>>> SearchProducts(
        [FromQuery] string? search,
        [FromQuery] long? categoryId,
        [FromQuery] string status = "Active",
        [FromQuery] string? attributeName = null,
        [FromQuery] string? attributeValue = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        if (!TryAuthorizeCatalogueRead(out var businessId))
            return Forbid();

        if (!Enum.TryParse<ProductStatus>(status, ignoreCase: true, out var productStatus)
            || !Enum.IsDefined(productStatus)
            || categoryId is <= 0
            || page is < 1 or > MaximumPage
            || pageSize is < 1 or > MaximumPageSize
            || search?.Length > 100
            || attributeName?.Length > 100
            || attributeValue?.Length > 200
            || (attributeName is null) != (attributeValue is null)
            || (attributeName is not null && string.IsNullOrWhiteSpace(attributeName))
            || (attributeValue is not null && string.IsNullOrWhiteSpace(attributeValue)))
        {
            return BadRequest(ApiResponse<MobileProductCataloguePageDto>.ErrorResponse(
                "INVALID_CATALOGUE_FILTER"));
        }

        if (!await deploymentGuard.IsReadyAsync(businessId, cancellationToken))
            return Forbid();

        var result = await catalogue.SearchAsync(
            new MobileProductCatalogueCriteria(
                search,
                categoryId,
                productStatus,
                attributeName,
                attributeValue,
                page,
                pageSize),
            cancellationToken);

        var response = new MobileProductCataloguePageDto(
            result.Items.Select(ToSummaryDto).ToArray(),
            result.Page,
            result.PageSize,
            result.TotalCount);
        return Ok(ApiResponse<MobileProductCataloguePageDto>.SuccessResponse(response));
    }

    [HttpGet("products/{productId:long}")]
    [ProducesResponseType(typeof(ApiResponse<MobileProductDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<MobileProductDetailDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<MobileProductDetailDto>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<MobileProductDetailDto>>> GetProduct(
        long productId,
        CancellationToken cancellationToken)
    {
        if (!TryAuthorizeCatalogueRead(out var businessId))
            return Forbid();
        if (productId <= 0)
            return BadRequest(ApiResponse<MobileProductDetailDto>.ErrorResponse("INVALID_PRODUCT_ID"));
        if (!await deploymentGuard.IsReadyAsync(businessId, cancellationToken))
            return Forbid();

        var product = await catalogue.FindProductAsync(productId, cancellationToken);
        return product is null
            ? NotFound(ApiResponse<MobileProductDetailDto>.ErrorResponse("PRODUCT_NOT_FOUND"))
            : Ok(ApiResponse<MobileProductDetailDto>.SuccessResponse(ToDetailDto(product)));
    }

    [HttpGet("products/{productId:long}/variants")]
    [ProducesResponseType(typeof(ApiResponse<MobileProductVariantPageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<MobileProductVariantPageDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<MobileProductVariantPageDto>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<MobileProductVariantPageDto>>> GetVariants(
        long productId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        if (!TryAuthorizeCatalogueRead(out var businessId))
            return Forbid();
        if (productId <= 0 || !IsValidPage(page, pageSize))
            return BadRequest(ApiResponse<MobileProductVariantPageDto>.ErrorResponse("INVALID_PAGINATION"));
        if (!await deploymentGuard.IsReadyAsync(businessId, cancellationToken))
            return Forbid();

        var result = await catalogue.FindVariantsAsync(productId, page, pageSize, cancellationToken);
        return result is null
            ? NotFound(ApiResponse<MobileProductVariantPageDto>.ErrorResponse("PRODUCT_NOT_FOUND"))
            : Ok(ApiResponse<MobileProductVariantPageDto>.SuccessResponse(new MobileProductVariantPageDto(
                result.Items.Select(ToVariantDto).ToArray(),
                result.Page,
                result.PageSize,
                result.TotalCount)));
    }

    [HttpGet("products/{productId:long}/variants/{variantId:long}")]
    [ProducesResponseType(typeof(ApiResponse<MobileProductVariantDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<MobileProductVariantDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<MobileProductVariantDto>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<MobileProductVariantDto>>> GetVariant(
        long productId,
        long variantId,
        CancellationToken cancellationToken)
    {
        if (!TryAuthorizeCatalogueRead(out var businessId))
            return Forbid();
        if (productId <= 0 || variantId <= 0)
            return BadRequest(ApiResponse<MobileProductVariantDto>.ErrorResponse("INVALID_PRODUCT_OR_VARIANT_ID"));
        if (!await deploymentGuard.IsReadyAsync(businessId, cancellationToken))
            return Forbid();

        var variant = await catalogue.FindVariantAsync(productId, variantId, cancellationToken);
        return variant is null
            ? NotFound(ApiResponse<MobileProductVariantDto>.ErrorResponse("VARIANT_NOT_FOUND"))
            : Ok(ApiResponse<MobileProductVariantDto>.SuccessResponse(ToVariantDto(variant)));
    }

    [HttpGet("barcodes/{barcode}")]
    [ProducesResponseType(typeof(ApiResponse<MobileProductBarcodeResolutionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<MobileProductBarcodeResolutionDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<MobileProductBarcodeResolutionDto>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<MobileProductBarcodeResolutionDto>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<MobileProductBarcodeResolutionDto>>> ResolveBarcode(
        string barcode,
        CancellationToken cancellationToken)
    {
        if (!TryAuthorizeCatalogueRead(out var businessId))
            return Forbid();
        if (string.IsNullOrEmpty(barcode) || barcode.Length > 50)
            return BadRequest(ApiResponse<MobileProductBarcodeResolutionDto>.ErrorResponse("INVALID_BARCODE"));
        if (!await deploymentGuard.IsReadyAsync(businessId, cancellationToken))
            return Forbid();

        var matches = await catalogue.FindBarcodeMatchesAsync(barcode, cancellationToken);
        if (matches.Count == 0)
            return NotFound(ApiResponse<MobileProductBarcodeResolutionDto>.ErrorResponse("BARCODE_NOT_FOUND"));
        if (matches.Count != 1)
        {
            logger.LogWarning("Mobile product barcode resolution denied because the exact code matched multiple records.");
            return Conflict(ApiResponse<MobileProductBarcodeResolutionDto>.ErrorResponse("AMBIGUOUS_BARCODE"));
        }

        var match = matches[0];
        return Ok(ApiResponse<MobileProductBarcodeResolutionDto>.SuccessResponse(
            new MobileProductBarcodeResolutionDto(
                match.MatchType,
                ToDetailDto(match.Product),
                match.Variant is null ? null : ToVariantDto(match.Variant))));
    }

    private bool TryAuthorizeCatalogueRead(out long businessId)
    {
        businessId = 0;
        if (roleSwitch.IsRoleSwitched
            || roleSwitch.IsSuperAdmin
            || !roleSwitch.EffectiveBusinessId.HasValue
            || !User.HasPermission("products.view")
            || !IsSupportedRole(roleSwitch.EffectiveRoleName))
        {
            logger.LogWarning("Mobile catalogue read denied for the current role or Business context.");
            return false;
        }

        businessId = roleSwitch.EffectiveBusinessId.Value;
        return true;
    }

    private static bool IsSupportedRole(string? role) =>
        string.Equals(role, "BusinessOwner", StringComparison.OrdinalIgnoreCase)
        || string.Equals(role, "SalesManager", StringComparison.OrdinalIgnoreCase)
        || string.Equals(role, "OutletManager", StringComparison.OrdinalIgnoreCase)
        || string.Equals(role, "Manager", StringComparison.OrdinalIgnoreCase);

    private static bool IsValidPage(int page, int pageSize) =>
        page is >= 1 and <= MaximumPage && pageSize is >= 1 and <= MaximumPageSize;

    private static MobileProductSummaryDto ToSummaryDto(MobileProductSummaryReadModel product) =>
        new(
            product.Id,
            product.Name,
            product.Description,
            product.ProductCode,
            product.Sku,
            product.ProductBarcode,
            product.CategoryId,
            product.CategoryName,
            product.Status,
            product.HasVariants,
            product.VariantCount);

    private static MobileProductDetailDto ToDetailDto(MobileProductReadModel product) =>
        new(
            product.Id,
            product.Name,
            product.Description,
            product.ProductCode,
            product.Sku,
            product.ProductBarcode,
            product.CategoryId,
            product.CategoryName,
            product.Status,
            product.HasVariants);

    private static MobileProductVariantDto ToVariantDto(MobileProductVariantReadModel variant)
    {
        using var attributes = JsonDocument.Parse(variant.Attributes);
        return new MobileProductVariantDto(
            variant.Id,
            variant.ProductId,
            variant.Name,
            variant.Sku,
            variant.Barcode,
            attributes.RootElement.Clone());
    }
}
