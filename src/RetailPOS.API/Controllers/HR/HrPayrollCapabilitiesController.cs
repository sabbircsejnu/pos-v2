using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.Application.HR.Capabilities;

namespace RetailPOS.API.Controllers.HR;

/// <summary>Discovers opt-in capabilities for the current validated Business.</summary>
[ApiController]
[Authorize]
[Route("api/hr/v1/capabilities")]
public sealed class HrPayrollCapabilitiesController(
    ISender sender, ILogger<HrPayrollCapabilitiesController> logger) : ControllerBase
{
    /// <summary>Returns effective HR/Payroll flags without enabling disabled modules.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(HrPayrollCapabilities), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<HrPayrollCapabilities>> Get(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await sender.Send(new GetHrPayrollCapabilitiesQuery(), cancellationToken));
        }
        catch (UnauthorizedAccessException)
        {
            logger.LogWarning("HR/Payroll capability discovery was refused.");
            return StatusCode(StatusCodes.Status403Forbidden,
                new { message = "You do not have permission to perform this action." });
        }
    }
}
