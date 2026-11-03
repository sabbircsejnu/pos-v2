using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.Authorization;
using RetailPOS.API.DTOs.Outlet;
using RetailPOS.Application.Outlets;
using RetailPOS.Application.Outlets.Commands;
using RetailPOS.Application.Outlets.Queries;
using MediatR;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "outlets.view")]
public class OutletsController : ControllerBase
{
    private readonly ILogger<OutletsController> _logger;
    private readonly ISender _sender;

    public OutletsController(
        ILogger<OutletsController> logger,
        ISender sender)
    {
        _logger = logger;
        _sender = sender;
    }

    /// <summary>
    /// Get all outlets
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<OutletDto>>> GetAllOutlets(CancellationToken cancellationToken)
    {
        try
        {
            var outlets = await _sender.Send(new GetOutletsQuery(), cancellationToken);
            return Ok(new { data = outlets.Select(MapOutlet), message = "Outlets retrieved successfully" });
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
    public async Task<ActionResult<OutletDto>> GetOutletById(long id, CancellationToken cancellationToken)
    {
        try
        {
            var outlet = await _sender.Send(new GetOutletByIdQuery(id), cancellationToken);
            if (outlet == null)
                return NotFound(new { error = $"Outlet with ID {id} not found" });

            return Ok(new { data = MapOutlet(outlet), message = "Outlet retrieved successfully" });
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
    public async Task<ActionResult<OutletDto>> CreateOutlet(
        [FromBody] CreateOutletDto dto,
        CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
                return BadRequest(new { error = "Outlet name is required" });

            if (string.IsNullOrWhiteSpace(dto.Address))
                return BadRequest(new { error = "Outlet address is required" });

            var result = await _sender.Send(
                new CreateOutletCommand(
                    dto.Name,
                    dto.Address,
                    dto.ContactNumber,
                    dto.ManagerId,
                    ToPosSetup(dto.InvoicePrefix, dto.TimeZoneId, dto.Terminals)),
                cancellationToken);
            var outlet = MapOutlet(result);
            return CreatedAtAction(
                nameof(GetOutletById),
                new { id = outlet.Id },
                new { data = outlet, message = "Outlet created successfully" });
        }
        catch (OutletSetupException ex)
        {
            return Conflict(new { error = ex.Message, errorCode = ex.Code });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = ex.Message, errorCode = "forbidden" });
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
    public async Task<ActionResult<OutletDto>> UpdateOutlet(
        long id,
        [FromBody] UpdateOutletDto dto,
        CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
                return BadRequest(new { error = "Outlet name is required" });

            if (string.IsNullOrWhiteSpace(dto.Address))
                return BadRequest(new { error = "Outlet address is required" });

            var result = await _sender.Send(
                new UpdateOutletCommand(
                    id,
                    dto.Name,
                    dto.Address,
                    dto.ContactNumber,
                    dto.ManagerId,
                    ToPosSetup(dto.InvoicePrefix, dto.TimeZoneId, dto.Terminals)),
                cancellationToken);
            var outlet = MapOutlet(result);
            return Ok(new { data = outlet, message = "Outlet updated successfully" });
        }
        catch (OutletSetupException ex)
        {
            return Conflict(new { error = ex.Message, errorCode = ex.Code });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = ex.Message, errorCode = "forbidden" });
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
    /// Delete a terminal that has no sales, cash-session or receipt history (otherwise deactivate it)
    /// </summary>
    [HttpDelete("{id}/terminals/{terminalId}")]
    [Authorize(Policy = "outlets.terminals.manage")]
    public async Task<ActionResult> DeleteTerminal(long id, long terminalId, CancellationToken cancellationToken)
    {
        try
        {
            await _sender.Send(new DeleteOutletTerminalCommand(id, terminalId), cancellationToken);
            return Ok(new { message = "Terminal deleted successfully" });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (OutletSetupException ex)
        {
            return Conflict(new { error = ex.Message, errorCode = ex.Code });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting terminal {TerminalId} of outlet {Id}", terminalId, id);
            return StatusCode(500, new { error = "An error occurred while deleting the terminal" });
        }
    }

    /// <summary>
    /// Delete an outlet
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Policy = "outlets.delete")]
    public async Task<ActionResult> DeleteOutlet(long id, CancellationToken cancellationToken)
    {
        try
        {
            var deleted = await _sender.Send(new DeleteOutletCommand(id), cancellationToken);
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
    public async Task<ActionResult<IEnumerable<OutletDto>>> SearchOutlets(
        [FromQuery] string q,
        CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(q))
                return BadRequest(new { error = "Search term is required" });

            var outlets = await _sender.Send(new GetOutletsQuery(q), cancellationToken);
            return Ok(new { data = outlets.Select(MapOutlet), message = "Search completed successfully" });
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
    public async Task<ActionResult<OutletStatsDto>> GetOutletStats(long id, CancellationToken cancellationToken)
    {
        try
        {
            var stats = await _sender.Send(new GetOutletStatsQuery(id), cancellationToken);
            if (stats == null)
                return NotFound(new { error = $"Outlet with ID {id} not found" });

            return Ok(new
            {
                data = new OutletStatsDto
                {
                    Id = stats.Id,
                    Name = stats.Name,
                    UserCount = stats.UserCount,
                    SalesCount = stats.SalesCount,
                    TotalSales = stats.TotalSales
                },
                message = "Statistics retrieved successfully"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving outlet statistics {Id}", id);
            return StatusCode(500, new { error = "An error occurred while retrieving statistics" });
        }
    }

    private OutletPosSetupInput ToPosSetup(string? invoicePrefix, string? timeZoneId, List<OutletTerminalDto>? terminals) =>
        new(
            invoicePrefix,
            timeZoneId,
            terminals?.Select(terminal => new OutletTerminalInput(terminal.Id, terminal.Name, terminal.IsActive)).ToList(),
            User.HasPermission("outlets.terminals.manage"));

    private static OutletDto MapOutlet(OutletReadModel outlet) => new()
    {
        Id = outlet.Id,
        Name = outlet.Name,
        Address = outlet.Address,
        ContactNumber = outlet.ContactNumber,
        ManagerId = outlet.ManagerId,
        ManagerName = outlet.ManagerName,
        UserCount = outlet.UserCount,
        CreatedAt = outlet.CreatedAt,
        UpdatedAt = outlet.UpdatedAt,
        InvoicePrefix = outlet.InvoicePrefix,
        InvoicePrefixLocked = outlet.InvoicePrefixLocked,
        TimeZoneId = outlet.TimeZoneId,
        Terminals = (outlet.Terminals ?? [])
            .Select(terminal => new OutletTerminalDto
            {
                Id = terminal.Id,
                Name = terminal.Name,
                Code = terminal.Code,
                IsActive = terminal.IsActive,
                HasHistory = terminal.HasHistory,
                HasOpenSession = terminal.HasOpenSession
            })
            .ToList()
    };
}
