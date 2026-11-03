using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.Application.HR.Payroll;

namespace RetailPOS.API.Controllers.Payroll;

[ApiController]
[Authorize]
[Route("api/payroll/v1/monetary-configurations")]
public sealed class PayrollMonetaryConfigurationsController(
    ISender sender,
    ILogger<PayrollMonetaryConfigurationsController> logger) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = "payroll.calculation_policy.view")]
    public Task<ActionResult<IReadOnlyList<PayrollMonetaryConfigurationResponse>>> List(
        CancellationToken cancellationToken) =>
        Execute<IReadOnlyList<PayrollMonetaryConfigurationResponse>>(async () =>
            Ok(await sender.Send(new GetPayrollMonetaryConfigurationsQuery(), cancellationToken)));

    [HttpPost]
    [Authorize(Policy = "payroll.calculation_policy.manage")]
    public Task<ActionResult<PayrollMonetaryConfigurationResponse>> Create(
        CreatePayrollMonetaryConfigurationRequest request, CancellationToken cancellationToken) =>
        Execute<PayrollMonetaryConfigurationResponse>(async () =>
        {
            var configuration = await sender.Send(
                new CreatePayrollMonetaryConfigurationCommand(request), cancellationToken);
            return CreatedAtAction(nameof(List), new { }, configuration);
        });

    private async Task<ActionResult<T>> Execute<T>(Func<Task<ActionResult<T>>> action)
    {
        try { return await action(); }
        catch (UnauthorizedAccessException exception)
        {
            logger.LogWarning("Payroll monetary configuration access rejected: {Message}",
                exception.Message);
            return Problem(statusCode: StatusCodes.Status403Forbidden,
                title: "Payroll monetary configuration access denied.");
        }
        catch (ArgumentException exception)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid Payroll monetary configuration.", detail: exception.Message);
        }
        catch (PayrollMonetaryConfigurationConflictException exception)
        {
            logger.LogWarning("Payroll monetary configuration conflict: {Message}",
                exception.Message);
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "Payroll monetary configuration conflict.", detail: exception.Message);
        }
    }
}
