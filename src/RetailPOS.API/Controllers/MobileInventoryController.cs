using RetailPOS.Application.Mobile;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.Authorization;
using RetailPOS.API.DTOs.Mobile;
using RetailPOS.API.Models;
using RetailPOS.API.Services;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.ReadModels.Mobile;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/mobile/v1/inventory")]
[Authorize]
public sealed class MobileInventoryController(
    IMobileInventoryReadStore inventory,
    IUserOutletAccessService outletAccess,
    IRoleSwitchContext roleSwitch,
    IMobileSingleBusinessReadinessGuard deploymentGuard,
    ILogger<MobileInventoryController> logger) : ControllerBase
{
    private const int MaximumPageSize = 100;
    private const int MaximumPage = 100_000;

    [HttpGet("outlets")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<MobileInventoryOutletDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<MobileInventoryOutletDto>>>> GetAuthorizedOutlets(
        CancellationToken cancellationToken)
    {
        if (!TryAuthorizeInventoryRead(out var businessId))
            return Forbid();
        if (!await deploymentGuard.IsReadyAsync(businessId, cancellationToken))
            return Forbid();

        var authorized = await GetAuthorizedOutletsAsync(businessId);
        if (authorized is null)
            return Forbid();

        var result = authorized.Outlets
            .Select(outlet => new MobileInventoryOutletDto(outlet.Id, outlet.Name))
            .ToArray();
        return Ok(ApiResponse<IReadOnlyList<MobileInventoryOutletDto>>.SuccessResponse(result));
    }

    [HttpGet("outlets/{outletId:long}/stock")]
    [ProducesResponseType(typeof(ApiResponse<MobileInventoryPageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<MobileInventoryPageDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<MobileInventoryPageDto>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<MobileInventoryPageDto>>> GetOutletStock(
        long outletId,
        [FromQuery] string? search,
        [FromQuery] long? productId,
        [FromQuery] long? variantId,
        [FromQuery] bool lowStockOnly = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        if (!TryAuthorizeInventoryRead(out var businessId))
            return Forbid();
        if (outletId <= 0
            || !IsValidPage(page, pageSize)
            || productId is <= 0
            || variantId is <= 0
            || search?.Length > 100)
        {
            return BadRequest(ApiResponse<MobileInventoryPageDto>.ErrorResponse("INVALID_INVENTORY_FILTER"));
        }
        if (lowStockOnly && !User.HasPermission("low_stock_alerts.view"))
            return Forbid();
        if (!await deploymentGuard.IsReadyAsync(businessId, cancellationToken))
            return Forbid();

        var authorized = await GetAuthorizedOutletsAsync(businessId);
        if (authorized is null)
            return Forbid();

        var outlet = authorized.Outlets.SingleOrDefault(location => location.Id == outletId);
        if (outlet is null)
        {
            logger.LogWarning("Mobile inventory read denied for an Outlet outside the caller's authorized Outlet set.");
            return Forbid();
        }

        var result = await inventory.SearchOutletStockAsync(
            new MobileInventoryCriteria(
                outletId,
                search,
                productId,
                variantId,
                lowStockOnly,
                page,
                pageSize),
            cancellationToken);

        var response = new MobileInventoryPageDto(
            result.Items.Select(item => ToDto(item, User.HasPermission("low_stock_alerts.view"))).ToArray(),
            result.Page,
            result.PageSize,
            result.TotalCount);
        return Ok(ApiResponse<MobileInventoryPageDto>.SuccessResponse(response));
    }

    private bool TryAuthorizeInventoryRead(out long businessId)
    {
        businessId = 0;
        if (roleSwitch.IsRoleSwitched
            || roleSwitch.IsSuperAdmin
            || !roleSwitch.EffectiveBusinessId.HasValue
            || !User.HasPermission("inventory.view")
            || !IsSupportedRole(roleSwitch.EffectiveRoleName))
        {
            logger.LogWarning("Mobile inventory read denied for the current role or Business context.");
            return false;
        }

        businessId = roleSwitch.EffectiveBusinessId.Value;
        return true;
    }

    private async Task<RetailPOS.API.DTOs.Outlet.AuthorizedOutletsDto?> GetAuthorizedOutletsAsync(long businessId)
    {
        try
        {
            return await outletAccess.GetAuthorizedOutletsForBusinessAsync(businessId);
        }
        catch (UnauthorizedAccessException exception)
        {
            logger.LogWarning("Mobile inventory read denied because Outlet access could not be resolved: {Reason}",
                exception.Message);
            return null;
        }
    }

    private static bool IsSupportedRole(string? role) =>
        string.Equals(role, "BusinessOwner", StringComparison.OrdinalIgnoreCase)
        || string.Equals(role, "SalesManager", StringComparison.OrdinalIgnoreCase)
        || string.Equals(role, "OutletManager", StringComparison.OrdinalIgnoreCase)
        || string.Equals(role, "Manager", StringComparison.OrdinalIgnoreCase);

    private static bool IsValidPage(int page, int pageSize) =>
        page is >= 1 and <= MaximumPage && pageSize is >= 1 and <= MaximumPageSize;

    private static MobileInventoryItemDto ToDto(
        MobileInventoryItemReadModel item,
        bool canViewLowStock)
    {
        using var attributes = JsonDocument.Parse(item.Attributes);
        return new MobileInventoryItemDto(
            item.InventoryId,
            item.ProductId,
            item.VariantId,
            item.ProductName,
            item.ProductCode,
            item.VariantName,
            item.Sku,
            item.Barcode,
            attributes.RootElement.Clone(),
            item.OutletId,
            item.OutletName,
            item.Quantity,
            canViewLowStock ? item.ReorderLevel : null,
            canViewLowStock ? item.IsLowStock : null);
    }
}
