using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.DTOs.Reports;
using RetailPOS.API.Models;
using RetailPOS.API.Services;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/reports/inventory")]
[Authorize]
public class InventoryReportsController : ControllerBase
{
    private readonly IInventoryReportService _inventoryReportService;

    public InventoryReportsController(IInventoryReportService inventoryReportService)
    {
        _inventoryReportService = inventoryReportService;
    }

    /// <summary>Returns current stock levels, optionally filtered by location or low-stock status</summary>
    [HttpGet("stock-levels")]
    public async Task<ActionResult<ApiResponse<List<StockLevelDto>>>> GetStockLevels(
        [FromQuery] long? locationId,
        [FromQuery] string? locationType,
        [FromQuery] bool lowStockOnly = false)
    {
        var result = await _inventoryReportService.GetStockLevelsAsync(locationId, locationType, lowStockOnly);
        return Ok(ApiResponse<List<StockLevelDto>>.SuccessResponse(result));
    }

    /// <summary>Returns total inventory valuation with per-category breakdown</summary>
    [HttpGet("valuation")]
    public async Task<ActionResult<ApiResponse<InventoryValuationDto>>> GetValuation()
    {
        var result = await _inventoryReportService.GetInventoryValuationAsync();
        return Ok(ApiResponse<InventoryValuationDto>.SuccessResponse(result));
    }

    /// <summary>Returns slow-moving inventory items</summary>
    [HttpGet("slow-moving")]
    public async Task<ActionResult<ApiResponse<List<SlowMovingItemDto>>>> GetSlowMoving(
        [FromQuery] int days = 90)
    {
        var result = await _inventoryReportService.GetSlowMovingItemsAsync(days);
        return Ok(ApiResponse<List<SlowMovingItemDto>>.SuccessResponse(result));
    }
}
