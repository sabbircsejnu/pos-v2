using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.DTOs.Inventory;
using RetailPOS.API.Models;
using RetailPOS.API.Services;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
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

    /// <summary>
    /// Get all inventory records
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<InventoryDto>>>> GetAll()
    {
        var inventories = await _inventoryService.GetAllAsync();
        return Ok(ApiResponse<List<InventoryDto>>.SuccessResponse(inventories));
    }

    /// <summary>
    /// Get inventory by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<InventoryDto>>> GetById(long id)
    {
        var inventory = await _inventoryService.GetByIdAsync(id);
        if (inventory == null)
            throw new KeyNotFoundException($"Inventory with ID {id} not found");

        return Ok(ApiResponse<InventoryDto>.SuccessResponse(inventory));
    }

    /// <summary>
    /// Get inventory for a specific outlet
    /// </summary>
    [HttpGet("outlet/{outletId}")]
    public async Task<ActionResult<ApiResponse<List<InventoryDto>>>> GetByOutlet(long outletId)
    {
        var inventories = await _inventoryService.GetByOutletAsync(outletId);
        return Ok(ApiResponse<List<InventoryDto>>.SuccessResponse(inventories));
    }

    /// <summary>
    /// Get inventory for a specific warehouse
    /// </summary>
    [HttpGet("warehouse/{warehouseId}")]
    public async Task<ActionResult<ApiResponse<List<InventoryDto>>>> GetByWarehouse(long warehouseId)
    {
        var inventories = await _inventoryService.GetByWarehouseAsync(warehouseId);
        return Ok(ApiResponse<List<InventoryDto>>.SuccessResponse(inventories));
    }

    /// <summary>
    /// Get low stock items with optional location filter
    /// </summary>
    [HttpGet("low-stock")]
    public async Task<ActionResult<ApiResponse<List<LowStockDto>>>> GetLowStock(
        [FromQuery] long? outletId = null,
        [FromQuery] long? warehouseId = null)
    {
        var lowStockItems = await _inventoryService.GetLowStockAsync(outletId, warehouseId);
        return Ok(ApiResponse<List<LowStockDto>>.SuccessResponse(lowStockItems, 
            $"Found {lowStockItems.Count} low stock items"));
    }

    /// <summary>
    /// Get out of stock items with optional location filter
    /// </summary>
    [HttpGet("out-of-stock")]
    public async Task<ActionResult<ApiResponse<List<InventoryDto>>>> GetOutOfStock(
        [FromQuery] long? outletId = null,
        [FromQuery] long? warehouseId = null)
    {
        var outOfStockItems = await _inventoryService.GetOutOfStockAsync(outletId, warehouseId);
        return Ok(ApiResponse<List<InventoryDto>>.SuccessResponse(outOfStockItems,
            $"Found {outOfStockItems.Count} out of stock items"));
    }

    /// <summary>
    /// Get items expiring within specified days
    /// </summary>
    [HttpGet("expiring-soon")]
    public async Task<ActionResult<ApiResponse<List<InventoryDto>>>> GetExpiringSoon(
        [FromQuery] int days = 30,
        [FromQuery] long? outletId = null,
        [FromQuery] long? warehouseId = null)
    {
        var expiringItems = await _inventoryService.GetExpiringSoonAsync(days, outletId, warehouseId);
        return Ok(ApiResponse<List<InventoryDto>>.SuccessResponse(expiringItems,
            $"Found {expiringItems.Count} items expiring within {days} days"));
    }

    /// <summary>
    /// Get total stock quantity for a specific product variant across all locations
    /// </summary>
    [HttpGet("variant/{variantId}/total-stock")]
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
    public async Task<ActionResult<ApiResponse<InventoryValuationDto>>> GetValuation(
        [FromQuery] long? outletId = null,
        [FromQuery] long? warehouseId = null)
    {
        var valuation = await _inventoryService.GetValuationAsync(outletId, warehouseId);
        return Ok(ApiResponse<InventoryValuationDto>.SuccessResponse(valuation, 
            "Inventory valuation calculated successfully"));
    }

    /// <summary>
    /// Get inventory summary grouped by locations (outlets and warehouses)
    /// </summary>
    [HttpGet("summary")]
    public async Task<ActionResult<ApiResponse<List<InventoryByLocationDto>>>> GetInventorySummary()
    {
        var summary = await _inventoryService.GetInventorySummaryByLocationsAsync();
        return Ok(ApiResponse<List<InventoryByLocationDto>>.SuccessResponse(summary, 
            $"Inventory summary for {summary.Count} locations"));
    }

    /// <summary>
    /// Advanced search with multiple filters
    /// </summary>
    [HttpPost("search")]
    public async Task<ActionResult<ApiResponse<List<InventoryDto>>>> Search([FromBody] InventorySearchDto searchDto)
    {
        var results = await _inventoryService.SearchAsync(searchDto);
        return Ok(ApiResponse<List<InventoryDto>>.SuccessResponse(results, 
            $"Found {results.Count} matching inventory records"));
    }

    /// <summary>
    /// Update stock thresholds (reorder level and max stock level)
    /// </summary>
    [HttpPut("{id}/threshold")]
    public async Task<ActionResult<ApiResponse<InventoryDto>>> UpdateStockThreshold(
        long id, 
        [FromBody] UpdateStockThresholdDto dto)
    {
        var inventory = await _inventoryService.UpdateStockThresholdAsync(id, dto);
        return Ok(ApiResponse<InventoryDto>.SuccessResponse(inventory, 
            "Stock thresholds updated successfully"));
    }
}
