using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.Application.HR.Organisation;

namespace RetailPOS.API.Controllers.HR;

[ApiController]
[Authorize]
[Route("api/hr/v1/positions")]
public sealed class HrPositionsController(
    ISender sender,
    ILogger<HrPositionsController> logger) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = "hr.position.view")]
    [ProducesResponseType(typeof(IReadOnlyList<HrPositionReadModel>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<HrPositionReadModel>>> GetPositions(
        [FromQuery] string? search,
        CancellationToken cancellationToken) =>
        await ExecuteAsync<IReadOnlyList<HrPositionReadModel>>(async () => Ok(await sender.Send(
            new GetHrPositionsQuery(search), cancellationToken)));

    [HttpPost]
    [Authorize(Policy = "hr.position.manage")]
    [ProducesResponseType(typeof(HrPositionReadModel), StatusCodes.Status201Created)]
    public async Task<ActionResult<HrPositionReadModel>> CreatePosition(
        [FromBody] HrPositionWriteData data,
        CancellationToken cancellationToken) =>
        await ExecuteAsync<HrPositionReadModel>(async () =>
        {
            var created = await sender.Send(new CreateHrPositionCommand(data), cancellationToken);
            return CreatedAtAction(nameof(GetPositions), new { }, created);
        });

    [HttpPut("{id:long}")]
    [Authorize(Policy = "hr.position.manage")]
    [ProducesResponseType(typeof(HrPositionReadModel), StatusCodes.Status200OK)]
    public async Task<ActionResult<HrPositionReadModel>> UpdatePosition(
        long id,
        [FromBody] UpdateHrPositionRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteAsync<HrPositionReadModel>(async () =>
        {
            var updated = await sender.Send(
                new UpdateHrPositionCommand(id, request.ExpectedVersion, request.Data), cancellationToken);
            return updated is null ? NotFound() : Ok(updated);
        });

    [HttpPost("{id:long}/deactivate")]
    [Authorize(Policy = "hr.position.manage")]
    [ProducesResponseType(typeof(HrPositionReadModel), StatusCodes.Status200OK)]
    public async Task<ActionResult<HrPositionReadModel>> DeactivatePosition(
        long id,
        [FromBody] DeactivateHrPositionRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteAsync<HrPositionReadModel>(async () =>
        {
            var result = await sender.Send(
                new DeactivateHrPositionCommand(id, request.ExpectedVersion, request.EffectiveTo),
                cancellationToken);
            return result is null ? NotFound() : Ok(result);
        });

    private async Task<ActionResult<T>> ExecuteAsync<T>(Func<Task<ActionResult<T>>> action)
    {
        try
        {
            return await action();
        }
        catch (UnauthorizedAccessException exception)
        {
            logger.LogWarning("HR position request rejected: {Message}", exception.Message);
            return StatusCode(StatusCodes.Status403Forbidden, new { message = exception.Message });
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
        catch (HrOrganisationConcurrencyException)
        {
            return Conflict(new { message = "Position was changed by another request." });
        }
        catch (InvalidOperationException exception)
        {
            logger.LogWarning("HR position operation conflicted: {Message}", exception.Message);
            return Conflict(new { message = exception.Message });
        }
    }
}

public sealed record UpdateHrPositionRequest(long ExpectedVersion, HrPositionWriteData Data);

public sealed record DeactivateHrPositionRequest(long ExpectedVersion, DateOnly EffectiveTo);
