using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.Application.HR.Payroll;

namespace RetailPOS.API.Controllers.Payroll;

[ApiController]
[Authorize]
[Route("api/payroll/v1/dashboard")]
public sealed class PayrollDashboardController(ISender sender, ILogger<PayrollDashboardController> logger)
    : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = "payroll.dashboard.view")]
    [Authorize(Policy = "payroll.salary.view")]
    [ProducesResponseType(typeof(PayrollDashboardSummary), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PayrollDashboardSummary>> Get(
        [FromQuery] long? periodId,
        [FromQuery] long? runId,
        [FromQuery] long? locationId,
        [FromQuery] DateOnly? fromDate,
        [FromQuery] DateOnly? toDate,
        [FromQuery] string? runType,
        CancellationToken cancellationToken)
    {
        try
        {
            var summary = await sender.Send(new GetPayrollDashboardQuery(
                periodId, runId, locationId, fromDate, toDate, runType), cancellationToken);
            return Ok(summary);
        }
        catch (UnauthorizedAccessException exception)
        {
            logger.LogWarning("Payroll dashboard access rejected: {Message}", exception.Message);
            return Problem(statusCode: StatusCodes.Status403Forbidden,
                title: "Payroll dashboard access denied.");
        }
        catch (ArgumentException exception)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid Payroll dashboard filters.", detail: exception.Message);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }
}
