using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.Application.HR.Organisation;

namespace RetailPOS.API.Controllers.HR;

[ApiController]
[Authorize]
[Route("api/hr/v1")]
public sealed class HrOrganisationController(
    ISender sender,
    ILogger<HrOrganisationController> logger) : ControllerBase
{
    [HttpGet("org-units")]
    [Authorize(Policy = "hr.organisation.view")]
    [ProducesResponseType(typeof(IReadOnlyList<HrOrgUnitReadModel>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<HrOrgUnitReadModel>>> GetOrgUnits(
        [FromQuery] string? search,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await sender.Send(new GetHrOrgUnitsQuery(search), cancellationToken));
        }
        catch (UnauthorizedAccessException exception)
        {
            return Forbidden(exception);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpGet("operational-locations")]
    [Authorize(Policy = "hr.location.view")]
    [ProducesResponseType(typeof(IReadOnlyList<OperationalLocationReadModel>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<OperationalLocationReadModel>>> GetOperationalLocations(
        [FromQuery] string? sourceEntityType,
        [FromQuery] string? search,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await sender.Send(
                new GetOperationalLocationsQuery(sourceEntityType, search), cancellationToken));
        }
        catch (UnauthorizedAccessException exception)
        {
            return Forbidden(exception);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpPost("org-units")]
    [Authorize(Policy = "hr.organisation.manage")]
    [ProducesResponseType(typeof(HrOrgUnitReadModel), StatusCodes.Status201Created)]
    public async Task<ActionResult<HrOrgUnitReadModel>> CreateOrgUnit(
        [FromBody] HrOrgUnitWriteData data,
        CancellationToken cancellationToken)
    {
        try
        {
            var created = await sender.Send(new CreateHrOrgUnitCommand(data), cancellationToken);
            return CreatedAtAction(nameof(GetOrgUnits), new { }, created);
        }
        catch (UnauthorizedAccessException exception)
        {
            return Forbidden(exception);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            logger.LogWarning("HR organisation-unit create conflicted: {Message}", exception.Message);
            return Conflict(new { message = exception.Message });
        }
    }

    [HttpPut("org-units/{id:long}")]
    [Authorize(Policy = "hr.organisation.manage")]
    [ProducesResponseType(typeof(HrOrgUnitReadModel), StatusCodes.Status200OK)]
    public async Task<ActionResult<HrOrgUnitReadModel>> UpdateOrgUnit(
        long id,
        [FromBody] UpdateHrOrgUnitRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var updated = await sender.Send(
                new UpdateHrOrgUnitCommand(id, request.ExpectedVersion, request.Data), cancellationToken);
            return updated is null ? NotFound() : Ok(updated);
        }
        catch (UnauthorizedAccessException exception)
        {
            return Forbidden(exception);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
        catch (HrOrganisationConcurrencyException)
        {
            return Conflict(new { message = "Organisation unit was changed by another request." });
        }
        catch (InvalidOperationException exception)
        {
            logger.LogWarning("HR organisation-unit update conflicted: {Message}", exception.Message);
            return Conflict(new { message = exception.Message });
        }
    }

    [HttpGet("operational-locations/{id:long}")]
    [Authorize(Policy = "hr.location.view")]
    [ProducesResponseType(typeof(OperationalLocationReadModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OperationalLocationReadModel>> GetOperationalLocation(
        long id,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await sender.Send(new GetOperationalLocationQuery(id), cancellationToken);
            return result is null ? NotFound() : Ok(result);
        }
        catch (UnauthorizedAccessException exception)
        {
            return Forbidden(exception);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpPost("operational-locations/reconcile")]
    [Authorize(Policy = "hr.location.manage")]
    [ProducesResponseType(typeof(OperationalLocationReconciliationResult), StatusCodes.Status200OK)]
    public async Task<ActionResult<OperationalLocationReconciliationResult>> ReconcileOperationalLocations(
        [FromQuery] int pageSize = 250,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return Ok(await sender.Send(new ReconcileOperationalLocationsCommand(pageSize), cancellationToken));
        }
        catch (UnauthorizedAccessException exception)
        {
            return Forbidden(exception);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    private ActionResult Forbidden(UnauthorizedAccessException exception)
    {
        logger.LogWarning("HR organisation request rejected: {Message}", exception.Message);
        return StatusCode(StatusCodes.Status403Forbidden, new { message = exception.Message });
    }
}

public sealed record UpdateHrOrgUnitRequest(long ExpectedVersion, HrOrgUnitWriteData Data);
