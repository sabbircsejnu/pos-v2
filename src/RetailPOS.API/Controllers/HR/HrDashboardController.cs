using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.Application.HR.Dashboard;

namespace RetailPOS.API.Controllers.HR;

[ApiController]
[Authorize]
[Route("api/hr/v1/dashboard")]
public sealed class HrDashboardController(
    ISender sender,
    ILogger<HrDashboardController> logger) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = "hr.dashboard.view")]
    [ProducesResponseType(typeof(HrDashboardSummary), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<HrDashboardSummary>> Get(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] long? locationProjectionId,
        [FromQuery] string? locationType,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await sender.Send(
                new HrDashboardQuery(from, to, locationProjectionId, locationType), cancellationToken));
        }
        catch (UnauthorizedAccessException exception)
        {
            logger.LogWarning("HR dashboard access rejected: {Message}", exception.Message);
            return Problem(statusCode: StatusCodes.Status403Forbidden,
                title: "Dashboard access denied.", detail: exception.Message);
        }
        catch (ArgumentException exception)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid dashboard filters.", detail: exception.Message);
        }
    }
}
