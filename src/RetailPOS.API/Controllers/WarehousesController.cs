using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.DTOs.Warehouse;
using RetailPOS.API.Services;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "warehouses.view")]
public class WarehousesController : ControllerBase
{
    private readonly IWarehouseService _warehouseService;
    private readonly ILogger<WarehousesController> _logger;

    public WarehousesController(IWarehouseService warehouseService, ILogger<WarehousesController> logger)
    {
        _warehouseService = warehouseService;
        _logger = logger;
    }

    /// <summary>
    /// Get all warehouses
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<WarehouseDto>>> GetAllWarehouses()
    {
        try
        {
            var warehouses = await _warehouseService.GetAllWarehousesAsync();
            return Ok(new { data = warehouses, message = "Warehouses retrieved successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving warehouses");
            return StatusCode(500, new { error = "An error occurred while retrieving warehouses" });
        }
    }

    /// <summary>
    /// Get warehouse by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<WarehouseDto>> GetWarehouseById(long id)
    {
        try
        {
            var warehouse = await _warehouseService.GetWarehouseByIdAsync(id);
            if (warehouse == null)
                return NotFound(new { error = $"Warehouse with ID {id} not found" });

            return Ok(new { data = warehouse, message = "Warehouse retrieved successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving warehouse {Id}", id);
            return StatusCode(500, new { error = "An error occurred while retrieving the warehouse" });
        }
    }

    /// <summary>
    /// Create a new warehouse
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "warehouses.create")]
    public async Task<ActionResult<WarehouseDto>> CreateWarehouse([FromBody] CreateWarehouseDto dto)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
                return BadRequest(new { error = "Warehouse name is required" });

            if (string.IsNullOrWhiteSpace(dto.Address))
                return BadRequest(new { error = "Warehouse address is required" });

            var warehouse = await _warehouseService.CreateWarehouseAsync(dto);
            return CreatedAtAction(
                nameof(GetWarehouseById),
                new { id = warehouse.Id },
                new { data = warehouse, message = "Warehouse created successfully" });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating warehouse");
            return StatusCode(500, new { error = "An error occurred while creating the warehouse" });
        }
    }

    /// <summary>
    /// Update an existing warehouse
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Policy = "warehouses.edit")]
    public async Task<ActionResult<WarehouseDto>> UpdateWarehouse(long id, [FromBody] UpdateWarehouseDto dto)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
                return BadRequest(new { error = "Warehouse name is required" });

            if (string.IsNullOrWhiteSpace(dto.Address))
                return BadRequest(new { error = "Warehouse address is required" });

            var warehouse = await _warehouseService.UpdateWarehouseAsync(id, dto);
            return Ok(new { data = warehouse, message = "Warehouse updated successfully" });
        }
        catch (InvalidOperationException ex)
        {
            if (ex.Message.Contains("not found"))
                return NotFound(new { error = ex.Message });
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating warehouse {Id}", id);
            return StatusCode(500, new { error = "An error occurred while updating the warehouse" });
        }
    }

    /// <summary>
    /// Delete a warehouse
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Policy = "warehouses.delete")]
    public async Task<ActionResult> DeleteWarehouse(long id)
    {
        try
        {
            var deleted = await _warehouseService.DeleteWarehouseAsync(id);
            if (!deleted)
                return NotFound(new { error = $"Warehouse with ID {id} not found" });

            return Ok(new { message = "Warehouse deleted successfully" });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting warehouse {Id}", id);
            return StatusCode(500, new { error = "An error occurred while deleting the warehouse" });
        }
    }

    /// <summary>
    /// Search warehouses by name or address
    /// </summary>
    [HttpGet("search")]
    public async Task<ActionResult<IEnumerable<WarehouseDto>>> SearchWarehouses([FromQuery] string q)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(q))
                return BadRequest(new { error = "Search term is required" });

            var warehouses = await _warehouseService.SearchWarehousesAsync(q);
            return Ok(new { data = warehouses, message = "Search completed successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching warehouses");
            return StatusCode(500, new { error = "An error occurred while searching warehouses" });
        }
    }

    /// <summary>
    /// Get warehouse statistics
    /// </summary>
    [HttpGet("{id}/stats")]
    public async Task<ActionResult<WarehouseStatsDto>> GetWarehouseStats(long id)
    {
        try
        {
            var stats = await _warehouseService.GetWarehouseStatsAsync(id);
            if (stats == null)
                return NotFound(new { error = $"Warehouse with ID {id} not found" });

            return Ok(new { data = stats, message = "Statistics retrieved successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving warehouse statistics {Id}", id);
            return StatusCode(500, new { error = "An error occurred while retrieving statistics" });
        }
    }
}
