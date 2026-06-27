using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.Authorization;
using RetailPOS.API.DTOs.Inventory;
using RetailPOS.API.Models;
using RetailPOS.API.Services;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class InventoryController : ControllerBase
{
    private readonly IInventoryService _inventoryService;
    private readonly ILogger<InventoryController> _logger;

    public InventoryController(
        IInventoryService inventoryService,
        ILogger<InventoryController> logger)
    {
        _inventoryService = inventoryService;
        _logger = logger;
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
    }
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Get all inventory records
    /// </summary>
    [HttpGet]
    [Authorize(Policy = "inventory.view")]
    public async Task<ActionResult<ApiResponse<List<InventoryDto>>>> GetAll()
    {
        var inventories = await _inventoryService.GetAllAsync();
        RedactCost(inventories);
        return Ok(ApiResponse<List<InventoryDto>>.SuccessResponse(inventories));
    }

    /// <summary>
    /// Get inventory by ID
    /// </summary>
    [HttpGet("{id}")]
    [Authorize(Policy = "inventory.view")]
    public async Task<ActionResult<ApiResponse<InventoryDto>>> GetById(long id)
    {
        var inventory = await _inventoryService.GetByIdAsync(id);
        if (inventory == null)
            throw new KeyNotFoundException($"Inventory with ID {id} not found");

        RedactCost(inventory);
        return Ok(ApiResponse<InventoryDto>.SuccessResponse(inventory));
    }

    /// <summary>
    /// Get inventory for a specific outlet
    /// </summary>
    [HttpGet("outlet/{outletId}")]
    [Authorize(Policy = "inventory.view")]
    public async Task<ActionResult<ApiResponse<List<InventoryDto>>>> GetByOutlet(long outletId)
    {
        var inventories = await _inventoryService.GetByOutletAsync(outletId);
        RedactCost(inventories);
        return Ok(ApiResponse<List<InventoryDto>>.SuccessResponse(inventories));
    }

    /// <summary>
    /// Get inventory for a specific warehouse
    /// </summary>
    [HttpGet("warehouse/{warehouseId}")]
    [Authorize(Policy = "inventory.view")]
    public async Task<ActionResult<ApiResponse<List<InventoryDto>>>> GetByWarehouse(long warehouseId)
    {
        var inventories = await _inventoryService.GetByWarehouseAsync(warehouseId);
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
        [FromQuery] long? warehouseId = null)
    {
        await ValidateInventoryFiltersAsync(outletId, warehouseId);
        var lowStockItems = await _inventoryService.GetLowStockAsync(outletId, warehouseId);
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
        [FromQuery] long? warehouseId = null)
    {
        await ValidateInventoryFiltersAsync(outletId, warehouseId);
        var outOfStockItems = await _inventoryService.GetOutOfStockAsync(outletId, warehouseId);
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
        [FromQuery] long? warehouseId = null)
    {
        await ValidateInventoryFiltersAsync(outletId, warehouseId);
        var expiringItems = await _inventoryService.GetExpiringSoonAsync(days, outletId, warehouseId);
        RedactCost(expiringItems);
        return Ok(ApiResponse<List<InventoryDto>>.SuccessResponse(expiringItems,
            $"Found {expiringItems.Count} items expiring within {days} days"));
    }

    /// <summary>
    /// Get total stock quantity for a specific product variant across all locations
    /// </summary>
    [HttpGet("variant/{variantId}/total-stock")]
    [Authorize(Policy = "inventory.view")]
    public async Task<ActionResult<ApiResponse<object>>> GetTotalStockByVariant(long variantId)
    {
        var totalStock = await _inventoryService.GetTotalStockByVariantAsync(variantId);
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
        [FromQuery] long? warehouseId = null)
    {
        await ValidateInventoryFiltersAsync(outletId, warehouseId);
        var valuation = await _inventoryService.GetValuationAsync(outletId, warehouseId);
        RedactCost(valuation);
        return Ok(ApiResponse<InventoryValuationDto>.SuccessResponse(valuation, 
            "Inventory valuation calculated successfully"));
    }

    /// <summary>
    /// Get inventory summary grouped by locations (outlets and warehouses)
    /// </summary>
    [HttpGet("summary")]
    [Authorize(Policy = "inventory.view")]
    public async Task<ActionResult<ApiResponse<List<InventoryByLocationDto>>>> GetInventorySummary()
    {
        var summary = await _inventoryService.GetInventorySummaryByLocationsAsync();
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
    public async Task<ActionResult<ApiResponse<List<InventoryDto>>>> Search([FromBody] InventorySearchDto searchDto)
    {
        await ValidateInventoryFiltersAsync(searchDto.OutletId, searchDto.WarehouseId);
        var results = await _inventoryService.SearchAsync(searchDto);
        RedactCost(results);
        return Ok(ApiResponse<List<InventoryDto>>.SuccessResponse(results, 
            $"Found {results.Count} matching inventory records"));
    }

    /// <summary>
    /// Update stock thresholds (reorder level and max stock level)
    /// </summary>
    [HttpPut("{id}/threshold")]
    [Authorize(Policy = "inventory.edit")]
    public async Task<ActionResult<ApiResponse<InventoryDto>>> UpdateStockThreshold(
        long id, 
        [FromBody] UpdateStockThresholdDto dto)
    {
        var existing = await _inventoryService.GetByIdAsync(id);
        if (existing == null)
            throw new KeyNotFoundException($"Inventory with ID {id} not found");

        var inventory = await _inventoryService.UpdateStockThresholdAsync(id, dto);
        return Ok(ApiResponse<InventoryDto>.SuccessResponse(inventory, 
            "Stock thresholds updated successfully"));
    }

    private async Task ValidateInventoryFiltersAsync(long? outletId, long? warehouseId)
    {
        if (outletId.HasValue && warehouseId.HasValue)
            throw new InvalidOperationException("Specify either outletId or warehouseId, not both.");
    }
}
