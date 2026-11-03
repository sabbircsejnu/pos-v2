using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.Application.HR.Payroll;
using RetailPOS.Infrastructure.HR.Payroll;

namespace RetailPOS.API.Controllers.Payroll;

[ApiController]
[Authorize]
[Route("api/payroll/v1/calculation-policies")]
public sealed class PayrollCalculationPoliciesController(
    ISender sender,
    ILogger<PayrollCalculationPoliciesController> logger) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = "payroll.calculation_policy.view")]
    public Task<ActionResult<IReadOnlyList<PayrollCalculationPolicyResponse>>> List(
        CancellationToken cancellationToken) =>
        Execute<IReadOnlyList<PayrollCalculationPolicyResponse>>(async () => Ok(await sender.Send(
            new GetPayrollCalculationPoliciesQuery(), cancellationToken)));

    [HttpPost]
    [Authorize(Policy = "payroll.calculation_policy.manage")]
    public Task<ActionResult<PayrollCalculationPolicyResponse>> Create(
        CreatePayrollCalculationPolicyRequest request, CancellationToken cancellationToken) =>
        Execute<PayrollCalculationPolicyResponse>(async () =>
        {
            var policy = await sender.Send(new CreatePayrollCalculationPolicyCommand(request), cancellationToken);
            return CreatedAtAction(nameof(List), new { }, policy);
        });

    private async Task<ActionResult<T>> Execute<T>(Func<Task<ActionResult<T>>> action)
    {
        try { return await action(); }
        catch (UnauthorizedAccessException exception)
        {
            logger.LogWarning("Payroll calculation policy access rejected: {Message}", exception.Message);
            return Problem(statusCode: StatusCodes.Status403Forbidden,
                title: "Payroll calculation policy access denied.");
        }
        catch (ArgumentException exception)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid Payroll calculation policy.", detail: exception.Message);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (PayrollCalculationPolicyConflictException exception)
        {
            logger.LogWarning("Payroll calculation policy conflict: {Message}", exception.Message);
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "Payroll calculation policy conflict.", detail: exception.Message);
        }
    }
}
