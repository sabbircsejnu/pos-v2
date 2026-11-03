using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.DTOs.Warehouse;
using RetailPOS.Application.Warehouses;
using RetailPOS.Application.Warehouses.Commands;
using RetailPOS.Application.Warehouses.Queries;
using MediatR;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "warehouses.view")]
public class WarehousesController : ControllerBase
{
    private readonly ILogger<WarehousesController> _logger;
    private readonly ISender _sender;

    public WarehousesController(
        ILogger<WarehousesController> logger,
        ISender sender)
    {
        _logger = logger;
        _sender = sender;
    }

    /// <summary>
    /// Get all warehouses
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<WarehouseDto>>> GetAllWarehouses(
        CancellationToken cancellationToken)
    {
        try
        {
            var warehouses = await _sender.Send(new GetWarehousesQuery(), cancellationToken);
            return Ok(new { data = warehouses.Select(MapWarehouse), message = "Warehouses retrieved successfully" });
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
    public async Task<ActionResult<WarehouseDto>> GetWarehouseById(
        long id,
        CancellationToken cancellationToken)
    {
        try
        {
            var warehouse = await _sender.Send(new GetWarehouseByIdQuery(id), cancellationToken);
            if (warehouse == null)
                return NotFound(new { error = $"Warehouse with ID {id} not found" });

            return Ok(new { data = MapWarehouse(warehouse), message = "Warehouse retrieved successfully" });
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
    public async Task<ActionResult<WarehouseDto>> CreateWarehouse(
        [FromBody] CreateWarehouseDto dto,
        CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
                return BadRequest(new { error = "Warehouse name is required" });

            if (string.IsNullOrWhiteSpace(dto.Address))
                return BadRequest(new { error = "Warehouse address is required" });

            var result = await _sender.Send(
                new CreateWarehouseCommand(dto.Name, dto.Address, dto.Capacity, dto.ManagerId),
                cancellationToken);
            var warehouse = MapWarehouse(result);
            _logger.LogInformation("Warehouse created: ID={Id}, Name={Name}", warehouse.Id, warehouse.Name);
            return CreatedAtAction(
                nameof(GetWarehouseById),
                new { id = warehouse.Id },
                new { data = warehouse, message = "Warehouse created successfully" });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Warehouse creation failed");
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
    public async Task<ActionResult<WarehouseDto>> UpdateWarehouse(
        long id,
        [FromBody] UpdateWarehouseDto dto,
        CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
                return BadRequest(new { error = "Warehouse name is required" });

            if (string.IsNullOrWhiteSpace(dto.Address))
                return BadRequest(new { error = "Warehouse address is required" });

            var result = await _sender.Send(
                new UpdateWarehouseCommand(id, dto.Name, dto.Address, dto.Capacity, dto.ManagerId),
                cancellationToken);
            var warehouse = MapWarehouse(result);
            _logger.LogInformation("Warehouse updated: ID={Id}, Name={Name}", warehouse.Id, warehouse.Name);
            return Ok(new { data = warehouse, message = "Warehouse updated successfully" });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Warehouse update failed for {Id}", id);
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
    public async Task<ActionResult> DeleteWarehouse(long id, CancellationToken cancellationToken)
    {
        try
        {
            var deleted = await _sender.Send(new DeleteWarehouseCommand(id), cancellationToken);
            if (!deleted)
            {
                _logger.LogWarning("Warehouse deletion failed: Warehouse with ID {Id} not found", id);
                return NotFound(new { error = $"Warehouse with ID {id} not found" });
            }

            _logger.LogInformation("Warehouse deleted: ID={Id}", id);
            return Ok(new { message = "Warehouse deleted successfully" });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Warehouse deletion failed for {Id}", id);
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
    public async Task<ActionResult<IEnumerable<WarehouseDto>>> SearchWarehouses(
        [FromQuery] string q,
        CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(q))
                return BadRequest(new { error = "Search term is required" });

            var warehouses = await _sender.Send(new GetWarehousesQuery(q), cancellationToken);
            return Ok(new { data = warehouses.Select(MapWarehouse), message = "Search completed successfully" });
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
    public async Task<ActionResult<WarehouseStatsDto>> GetWarehouseStats(
        long id,
        CancellationToken cancellationToken)
    {
        try
        {
            var stats = await _sender.Send(new GetWarehouseStatsQuery(id), cancellationToken);
            if (stats == null)
                return NotFound(new { error = $"Warehouse with ID {id} not found" });

            return Ok(new { data = MapWarehouseStats(stats), message = "Statistics retrieved successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving warehouse statistics {Id}", id);
            return StatusCode(500, new { error = "An error occurred while retrieving statistics" });
        }
    }

    private static WarehouseDto MapWarehouse(WarehouseReadModel warehouse) => new()
    {
        Id = warehouse.Id,
        Name = warehouse.Name,
        Address = warehouse.Address,
        Capacity = warehouse.Capacity,
        ManagerId = warehouse.ManagerId,
        ManagerName = warehouse.ManagerName,
        PurchaseOrderCount = warehouse.PurchaseOrderCount,
        CreatedAt = warehouse.CreatedAt,
        UpdatedAt = warehouse.UpdatedAt
    };

    private static WarehouseStatsDto MapWarehouseStats(WarehouseStatsReadModel stats) => new()
    {
        Id = stats.Id,
        Name = stats.Name,
        Capacity = stats.Capacity,
        PurchaseOrderCount = stats.PurchaseOrderCount,
        CurrentStock = stats.CurrentStock,
        CapacityUtilization = stats.CapacityUtilization
    };
}
