using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.DTOs.Outlet;
using RetailPOS.API.Services;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "outlets.view")]
public class OutletsController : ControllerBase
{
    private readonly IOutletService _outletService;
    private readonly ILogger<OutletsController> _logger;

    public OutletsController(IOutletService outletService, ILogger<OutletsController> logger)
    {
        _outletService = outletService;
        _logger = logger;
    }

    /// <summary>
    /// Get all outlets
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<OutletDto>>> GetAllOutlets()
    {
        try
        {
            var outlets = await _outletService.GetAllOutletsAsync();
            return Ok(new { data = outlets, message = "Outlets retrieved successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving outlets");
            return StatusCode(500, new { error = "An error occurred while retrieving outlets" });
        }
    }

    /// <summary>
    /// Get outlet by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<OutletDto>> GetOutletById(long id)
    {
        try
        {
            var outlet = await _outletService.GetOutletByIdAsync(id);
            if (outlet == null)
                return NotFound(new { error = $"Outlet with ID {id} not found" });

            return Ok(new { data = outlet, message = "Outlet retrieved successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving outlet {Id}", id);
            return StatusCode(500, new { error = "An error occurred while retrieving the outlet" });
        }
    }

    /// <summary>
    /// Create a new outlet
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "outlets.create")]
    public async Task<ActionResult<OutletDto>> CreateOutlet([FromBody] CreateOutletDto dto)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
                return BadRequest(new { error = "Outlet name is required" });

            if (string.IsNullOrWhiteSpace(dto.Address))
                return BadRequest(new { error = "Outlet address is required" });

            var outlet = await _outletService.CreateOutletAsync(dto);
            return CreatedAtAction(
                nameof(GetOutletById),
                new { id = outlet.Id },
                new { data = outlet, message = "Outlet created successfully" });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating outlet");
            return StatusCode(500, new { error = "An error occurred while creating the outlet" });
        }
    }

    /// <summary>
    /// Update an existing outlet
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Policy = "outlets.edit")]
    public async Task<ActionResult<OutletDto>> UpdateOutlet(long id, [FromBody] UpdateOutletDto dto)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
                return BadRequest(new { error = "Outlet name is required" });

            if (string.IsNullOrWhiteSpace(dto.Address))
                return BadRequest(new { error = "Outlet address is required" });

            var outlet = await _outletService.UpdateOutletAsync(id, dto);
            return Ok(new { data = outlet, message = "Outlet updated successfully" });
        }
        catch (InvalidOperationException ex)
        {
            if (ex.Message.Contains("not found"))
                return NotFound(new { error = ex.Message });
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating outlet {Id}", id);
            return StatusCode(500, new { error = "An error occurred while updating the outlet" });
        }
    }

    /// <summary>
    /// Delete an outlet
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Policy = "outlets.delete")]
    public async Task<ActionResult> DeleteOutlet(long id)
    {
        try
        {
            var deleted = await _outletService.DeleteOutletAsync(id);
            if (!deleted)
                return NotFound(new { error = $"Outlet with ID {id} not found" });

            return Ok(new { message = "Outlet deleted successfully" });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting outlet {Id}", id);
            return StatusCode(500, new { error = "An error occurred while deleting the outlet" });
        }
    }

    /// <summary>
    /// Search outlets by name, address, or contact number
    /// </summary>
    [HttpGet("search")]
    public async Task<ActionResult<IEnumerable<OutletDto>>> SearchOutlets([FromQuery] string q)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(q))
                return BadRequest(new { error = "Search term is required" });

            var outlets = await _outletService.SearchOutletsAsync(q);
            return Ok(new { data = outlets, message = "Search completed successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching outlets");
            return StatusCode(500, new { error = "An error occurred while searching outlets" });
        }
    }

    /// <summary>
    /// Get outlet statistics
    /// </summary>
    [HttpGet("{id}/stats")]
    public async Task<ActionResult<OutletStatsDto>> GetOutletStats(long id)
    {
        try
        {
            var stats = await _outletService.GetOutletStatsAsync(id);
            if (stats == null)
                return NotFound(new { error = $"Outlet with ID {id} not found" });

            return Ok(new { data = stats, message = "Statistics retrieved successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving outlet statistics {Id}", id);
            return StatusCode(500, new { error = "An error occurred while retrieving statistics" });
        }
    }
}
