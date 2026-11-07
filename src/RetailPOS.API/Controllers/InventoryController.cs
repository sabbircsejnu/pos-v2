using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.Authorization;
using RetailPOS.API.DTOs.Inventory;
using RetailPOS.API.Middleware;
using RetailPOS.API.Models;
using RetailPOS.API.Services;
using RetailPOS.Application.Inventory.Commands;
using RetailPOS.Application.Inventory.ReadModels;
using RetailPOS.Application.Inventory.ReadModels.Alerts;
using RetailPOS.Application.Inventory.Queries;
using MediatR;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class InventoryController : ControllerBase
{
    private readonly IUserOutletAccessService _outletAccess;
    private readonly IRoleSwitchContext _roleSwitch;
    private readonly ILogger<InventoryController> _logger;
    private readonly ISender _sender;

    public InventoryController(
        IUserOutletAccessService outletAccess,
        IRoleSwitchContext roleSwitch,
        ILogger<InventoryController> logger,
        ISender sender)
    {
        _outletAccess = outletAccess;
        _roleSwitch = roleSwitch;
        _logger = logger;
        _sender = sender;
    }

    // ── Cost-visibility helper ───────────────────────────────────────────────
    private void RedactCost(InventoryDto dto)
    {
        if (User.CanViewCost()) return;
        dto.CostPrice = 0;
        dto.RetailPrice = dto.RetailPrice; // untouched
    }

    private void RedactCost(IEnumerable<InventoryDto> items)
    {
        if (User.CanViewCost()) return;
        foreach (var item in items) RedactCost(item);
    }

    private void RedactCost(InventoryValuationDto dto)
    {
        if (User.CanViewCost()) return;
        dto.TotalCostValue = 0;
        foreach (var cat in dto.CategoryBreakdown) cat.TotalCostValue = 0;
        // The store orders categories by cost; that order would still reveal which carry the most (U-13).
        dto.CategoryBreakdown = dto.CategoryBreakdown.OrderBy(cat => cat.CategoryName, StringComparer.OrdinalIgnoreCase).ToList();
    }
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Get all inventory records
    /// </summary>
    [HttpGet]
    [Authorize(Policy = "inventory.view")]
    public async Task<ActionResult<ApiResponse<List<InventoryDto>>>> GetAll(CancellationToken cancellationToken)
    {
        var readModels = await _sender.Send(new GetAllInventoryQuery(), cancellationToken);
        var inventories = readModels.Select(MapInventory).ToList();
        RedactCost(inventories);
        return Ok(ApiResponse<List<InventoryDto>>.SuccessResponse(inventories));
    }

    /// <summary>
    /// Get inventory by ID
    /// </summary>
    [HttpGet("{id}")]
    [Authorize(Policy = "inventory.view")]
    public async Task<ActionResult<ApiResponse<InventoryDto>>> GetById(
        long id,
        CancellationToken cancellationToken)
    {
        var readModel = await _sender.Send(new GetInventoryByIdQuery(id), cancellationToken);
        if (readModel is null)
            throw new KeyNotFoundException($"Inventory with ID {id} not found");

        var inventory = MapInventory(readModel);
        RedactCost(inventory);
        return Ok(ApiResponse<InventoryDto>.SuccessResponse(inventory));
    }

    private static InventoryDto MapInventory(InventoryReadModel model) => new()
    {
        Id = model.Id,
        ProductId = model.ProductId,
        ProductVariantId = model.ProductVariantId,
        ProductName = model.ProductName,
        ProductCode = model.ProductCode,
        VariantName = model.VariantName,
        VariantAttributes = model.VariantAttributes,
        PrimaryImageThumb = model.PrimaryImageThumb,
        Sku = model.Sku,
        Barcode = model.Barcode,
        OutletId = model.OutletId,
        OutletName = model.OutletName,
        WarehouseId = model.WarehouseId,
        WarehouseName = model.WarehouseName,
        LocationType = model.LocationType,
        Quantity = model.Quantity,
        ReorderLevel = model.ReorderLevel,
        BatchNumber = model.BatchNumber,
        ExpiryDate = model.ExpiryDate,
        CostPrice = model.CostPrice,
        RetailPrice = model.RetailPrice
    };

    private static LowStockDto MapLowStock(LowStockReadModel model) => new()
    {
        InventoryId = model.InventoryId,
        ProductVariantId = model.ProductVariantId,
        LocationId = model.LocationId,
        ProductName = model.ProductName,
        VariantName = model.VariantName,
        VariantAttributes = model.VariantAttributes,
        PrimaryImageThumb = model.PrimaryImageThumb,
        Sku = model.Sku,
        CurrentQuantity = model.CurrentQuantity,
        ReorderLevel = model.ReorderLevel,
        LocationName = model.LocationName,
        LocationType = model.LocationType
    };

    /// <summary>
    /// Get inventory for a specific outlet
    /// </summary>
    [HttpGet("outlet/{outletId}")]
    [Authorize(Policy = "inventory.view")]
    public async Task<ActionResult<ApiResponse<List<InventoryDto>>>> GetByOutlet(
        long outletId,
        CancellationToken cancellationToken)
    {
        var locationScope = await TryResolveInventoryLocationScopeAsync(outletId, null);
        if (locationScope is null)
            return InventoryLocationDenied();

        var readModels = await _sender.Send(
            new GetInventoryByOutletQuery(locationScope.SelectedOutletId!.Value),
            cancellationToken);
        var inventories = readModels.Select(MapInventory).ToList();
        RedactCost(inventories);
        return Ok(ApiResponse<List<InventoryDto>>.SuccessResponse(inventories));
    }

    /// <summary>
    /// Get inventory for a specific warehouse
    /// </summary>
    [HttpGet("warehouse/{warehouseId}")]
    [Authorize(Policy = "inventory.view")]
    public async Task<ActionResult<ApiResponse<List<InventoryDto>>>> GetByWarehouse(
        long warehouseId,
        CancellationToken cancellationToken)
    {
        var locationScope = await TryResolveInventoryLocationScopeAsync(null, warehouseId);
        if (locationScope is null)
            return InventoryLocationDenied();

        var readModels = await _sender.Send(
            new GetInventoryByWarehouseQuery(locationScope.SelectedWarehouseId!.Value),
            cancellationToken);
        var inventories = readModels.Select(MapInventory).ToList();
        RedactCost(inventories);
        return Ok(ApiResponse<List<InventoryDto>>.SuccessResponse(inventories));
    }

    /// <summary>
    /// Get low stock items with optional location filter
    /// </summary>
    [HttpGet("low-stock")]
    [Authorize(Policy = "low_stock_alerts.view")]
    public async Task<ActionResult<ApiResponse<List<LowStockDto>>>> GetLowStock(
        [FromQuery] long? outletId = null,
        [FromQuery] long? warehouseId = null,
        CancellationToken cancellationToken = default)
    {
        var locationScope = await TryResolveInventoryLocationScopeAsync(outletId, warehouseId);
        if (locationScope is null)
            return InventoryLocationDenied();

        var readModels = await _sender.Send(
            new GetLowStockInventoryQuery(locationScope),
            cancellationToken);
        var lowStockItems = readModels.Select(MapLowStock).ToList();
        return Ok(ApiResponse<List<LowStockDto>>.SuccessResponse(lowStockItems, 
            $"Found {lowStockItems.Count} low stock items"));
    }

    /// <summary>
    /// Get out of stock items with optional location filter
    /// </summary>
    [HttpGet("out-of-stock")]
    [Authorize(Policy = "inventory.view")]
    public async Task<ActionResult<ApiResponse<List<InventoryDto>>>> GetOutOfStock(
        [FromQuery] long? outletId = null,
        [FromQuery] long? warehouseId = null,
        CancellationToken cancellationToken = default)
    {
        var locationScope = await TryResolveInventoryLocationScopeAsync(outletId, warehouseId);
        if (locationScope is null)
            return InventoryLocationDenied();

        var readModels = await _sender.Send(
            new GetOutOfStockInventoryQuery(locationScope),
            cancellationToken);
        var outOfStockItems = readModels.Select(MapInventory).ToList();
        RedactCost(outOfStockItems);
        return Ok(ApiResponse<List<InventoryDto>>.SuccessResponse(outOfStockItems,
            $"Found {outOfStockItems.Count} out of stock items"));
    }

    /// <summary>
    /// Get items expiring within specified days
    /// </summary>
    [HttpGet("expiring-soon")]
    [Authorize(Policy = "inventory.view")]
    public async Task<ActionResult<ApiResponse<List<InventoryDto>>>> GetExpiringSoon(
        [FromQuery] int days = 30,
        [FromQuery] long? outletId = null,
        [FromQuery] long? warehouseId = null,
        CancellationToken cancellationToken = default)
    {
        var locationScope = await TryResolveInventoryLocationScopeAsync(outletId, warehouseId);
        if (locationScope is null)
            return InventoryLocationDenied();

        var readModels = await _sender.Send(
            new GetExpiringInventoryQuery(days, locationScope),
            cancellationToken);
        var expiringItems = readModels.Select(MapInventory).ToList();
        RedactCost(expiringItems);
        return Ok(ApiResponse<List<InventoryDto>>.SuccessResponse(expiringItems,
            $"Found {expiringItems.Count} items expiring within {days} days"));
    }

    /// <summary>
    /// Get total stock quantity for a specific product variant across all locations
    /// </summary>
    [HttpGet("variant/{variantId}/total-stock")]
    [Authorize(Policy = "inventory.view")]
    public async Task<ActionResult<ApiResponse<object>>> GetTotalStockByVariant(
        long variantId,
        CancellationToken cancellationToken)
    {
        var totalStock = await _sender.Send(
            new GetTotalStockByVariantQuery(variantId),
            cancellationToken);
        return Ok(ApiResponse<object>.SuccessResponse(new { 
            variantId, 
            totalQuantity = totalStock 
        }));
    }

    /// <summary>
    /// Get inventory valuation with optional location filter
    /// </summary>
    [HttpGet("valuation")]
    [Authorize(Policy = "inventory.view")]
    public async Task<ActionResult<ApiResponse<InventoryValuationDto>>> GetValuation(
        [FromQuery] long? outletId = null,
        [FromQuery] long? warehouseId = null,
        CancellationToken cancellationToken = default)
    {
        var locationScope = await TryResolveInventoryLocationScopeAsync(outletId, warehouseId);
        if (locationScope is null)
            return InventoryLocationDenied();

        var readModel = await _sender.Send(
            new GetInventoryValuationQuery(locationScope),
            cancellationToken);
        var valuation = new InventoryValuationDto
        {
            LocationName = readModel.LocationName,
            LocationType = readModel.LocationType,
            TotalItems = readModel.TotalItems,
            TotalQuantity = readModel.TotalQuantity,
            TotalCostValue = readModel.TotalCostValue,
            TotalRetailValue = readModel.TotalRetailValue,
            CategoryBreakdown = readModel.CategoryBreakdown.Select(category => new CategoryValuationDto
            {
                CategoryName = category.CategoryName,
                ProductCount = category.ProductCount,
                TotalQuantity = category.TotalQuantity,
                TotalCostValue = category.TotalCostValue,
                TotalRetailValue = category.TotalRetailValue
            }).ToList()
        };
        RedactCost(valuation);
        return Ok(ApiResponse<InventoryValuationDto>.SuccessResponse(valuation, 
            "Inventory valuation calculated successfully"));
    }

    /// <summary>
    /// Get inventory summary grouped by locations (outlets and warehouses)
    /// </summary>
    [HttpGet("summary")]
    [Authorize(Policy = "inventory.view")]
    public async Task<ActionResult<ApiResponse<List<InventoryByLocationDto>>>> GetInventorySummary(
        CancellationToken cancellationToken)
    {
        var readModels = await _sender.Send(new GetInventoryLocationSummaryQuery(), cancellationToken);
        var summary = readModels.Select(model => new InventoryByLocationDto
        {
            OutletId = model.OutletId,
            OutletName = model.OutletName,
            WarehouseId = model.WarehouseId,
            WarehouseName = model.WarehouseName,
            LocationType = model.LocationType,
            TotalProducts = model.TotalProducts,
            TotalQuantity = model.TotalQuantity,
            LowStockCount = model.LowStockCount,
            OutOfStockCount = model.OutOfStockCount,
            TotalValue = model.TotalValue
        }).ToList();
        // InventoryByLocationDto.TotalValue is cost-based; strip when not authorised
        if (!User.CanViewCost())
            foreach (var s in summary) s.TotalValue = 0;
        return Ok(ApiResponse<List<InventoryByLocationDto>>.SuccessResponse(summary, 
            $"Inventory summary for {summary.Count} locations"));
    }

    /// <summary>
    /// Advanced search with multiple filters
    /// </summary>
    [HttpPost("search")]
    [Authorize(Policy = "inventory.view")]
    public async Task<ActionResult<ApiResponse<List<InventoryDto>>>> Search(
        [FromBody] InventorySearchDto searchDto,
        CancellationToken cancellationToken)
    {
        var locationScope = await TryResolveInventoryLocationScopeAsync(
            searchDto.OutletId,
            searchDto.WarehouseId);
        if (locationScope is null)
            return InventoryLocationDenied();

        var criteria = new InventorySearchCriteria(
            searchDto.ProductSearch,
            searchDto.VariantId,
            searchDto.VariantSearch,
            searchDto.VariantAttributeFilters?
                .Select(filter => new VariantAttributeFilterCriteria(filter.AttributeName, filter.Values))
                .ToArray(),
            searchDto.CategoryId,
            locationScope,
            searchDto.LowStockOnly,
            searchDto.OutOfStockOnly,
            searchDto.ExpiringSoon,
            searchDto.ExpiringWithinDays,
            searchDto.Page,
            searchDto.PageSize);
        var readModels = await _sender.Send(new SearchInventoryQuery(criteria), cancellationToken);
        var results = readModels.Select(MapInventory).ToList();
        RedactCost(results);
        return Ok(ApiResponse<List<InventoryDto>>.SuccessResponse(results, 
            $"Found {results.Count} matching inventory records"));
    }

    /// <summary>
    /// Get dynamic variant-attribute filter options.
    /// Returns only active attributes/values from Variant Setup that are actually used by inventory variants.
    /// </summary>
    [HttpGet("variant-attribute-filters")]
    [Authorize(Policy = "inventory.view")]
    public async Task<ActionResult<ApiResponse<List<VariantAttributeFilterOptionsDto>>>> GetVariantAttributeFilterOptions(
        [FromQuery] long? outletId = null,
        [FromQuery] long? warehouseId = null,
        CancellationToken cancellationToken = default)
    {
        var locationScope = await TryResolveInventoryLocationScopeAsync(outletId, warehouseId);
        if (locationScope is null)
            return InventoryLocationDenied();

        var readModels = await _sender.Send(
            new GetVariantAttributeFilterOptionsQuery(locationScope),
            cancellationToken);
        var options = readModels.Select(readModel => new VariantAttributeFilterOptionsDto
        {
            AttributeName = readModel.AttributeName,
            Values = readModel.Values.ToList()
        }).ToList();
        return Ok(ApiResponse<List<VariantAttributeFilterOptionsDto>>.SuccessResponse(options));
    }

    /// <summary>
    /// Update stock thresholds (reorder level and max stock level)
    /// </summary>
    [HttpPut("{id}/threshold")]
    [Authorize(Policy = "inventory.edit")]
    public async Task<ActionResult<ApiResponse<InventoryDto>>> UpdateStockThreshold(
        long id,
        [FromBody] UpdateStockThresholdDto dto,
        CancellationToken cancellationToken)
    {
        var readModel = await _sender.Send(
            new UpdateStockThresholdCommand(id, dto.ReorderLevel),
            cancellationToken);
        if (readModel is null)
            throw new KeyNotFoundException($"Inventory with ID {id} not found");

        var inventory = MapInventory(readModel);
        return Ok(ApiResponse<InventoryDto>.SuccessResponse(inventory, 
            "Stock thresholds updated successfully"));
    }

    private static void ValidateInventoryFilters(long? outletId, long? warehouseId)
    {
        if (outletId.HasValue && warehouseId.HasValue)
            throw new InvalidOperationException("Specify either outletId or warehouseId, not both.");
    }

    private async Task<InventoryLocationScope?> TryResolveInventoryLocationScopeAsync(
        long? outletId,
        long? warehouseId)
    {
        try
        {
            ValidateInventoryFilters(outletId, warehouseId);
            var businessScopeId = GetValidatedBusinessContextForSuperAdmin();
            var authorized = businessScopeId.HasValue
                ? await _outletAccess.GetAuthorizedOutletsForBusinessAsync(businessScopeId.Value)
                : await _outletAccess.GetAuthorizedOutletsAsync();

            if (outletId.HasValue)
            {
                var (locationId, locationType) =
                    await _outletAccess.ResolveAndAuthorizeLocationAsync(outletId, "outlet");
                if (locationId != outletId ||
                    locationType != "outlet" ||
                    !authorized.Outlets.Any(location => location.Id == outletId.Value))
                    throw new UnauthorizedAccessException("The requested location is not an authorized Outlet.");

                return new InventoryLocationScope(
                    new[] { outletId.Value },
                    Array.Empty<long>(),
                    SelectedOutletId: outletId.Value);
            }

            if (warehouseId.HasValue)
            {
                var (locationId, locationType) =
                    await _outletAccess.ResolveAndAuthorizeLocationAsync(warehouseId, "warehouse");
                if (locationId != warehouseId ||
                    locationType != "warehouse" ||
                    !authorized.Warehouses.Any(location => location.Id == warehouseId.Value))
                    throw new UnauthorizedAccessException("The requested location is not an authorized Warehouse.");

                return new InventoryLocationScope(
                    Array.Empty<long>(),
                    new[] { warehouseId.Value },
                    SelectedWarehouseId: warehouseId.Value);
            }

            return new InventoryLocationScope(
                authorized.Outlets.Select(location => location.Id).ToArray(),
                authorized.Warehouses.Select(location => location.Id).ToArray());
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger.LogWarning(exception, "Inventory location access denied for request {Path}", Request.Path);
            return null;
        }
    }

    private ObjectResult InventoryLocationDenied() =>
        StatusCode(
            StatusCodes.Status403Forbidden,
            ApiResponse.ErrorResponse("The requested inventory location is not authorized."));

    private long? GetValidatedBusinessContextForSuperAdmin()
    {
        if (!_roleSwitch.IsSuperAdmin)
            return null;

        if (!HttpContext.Items.TryGetValue(BusinessContextMiddleware.ItemName, out var validatedBusinessId) ||
            validatedBusinessId is not long selectedBusinessId ||
            _roleSwitch.EffectiveBusinessId != selectedBusinessId)
        {
            throw new UnauthorizedAccessException(
                "An explicitly selected Business context is required for Super Admin inventory access.");
        }

        return selectedBusinessId;
    }
}
