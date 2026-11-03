using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.Application.HR.Payroll;

namespace RetailPOS.API.Controllers.Payroll;

[ApiController]
[Authorize]
[Route("api/payroll/v1/periods")]
public sealed class PayrollPeriodsController(
    ISender sender,
    ILogger<PayrollPeriodsController> logger) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = "payroll.period.view")]
    public Task<ActionResult<IReadOnlyList<PayrollPeriodResponse>>> List(CancellationToken cancellationToken) =>
        Execute<IReadOnlyList<PayrollPeriodResponse>>(async () =>
            Ok(await sender.Send(new GetPayrollPeriodsQuery(), cancellationToken)));

    [HttpGet("{id:long}")]
    [Authorize(Policy = "payroll.period.view")]
    public Task<ActionResult<PayrollPeriodResponse>> Get(long id, CancellationToken cancellationToken) =>
        Execute<PayrollPeriodResponse>(async () =>
            Ok(await sender.Send(new GetPayrollPeriodQuery(id), cancellationToken)));

    [HttpPost]
    [Authorize(Policy = "payroll.period.create")]
    public Task<ActionResult<PayrollPeriodResponse>> Create(
        CreatePayrollPeriodRequest request, CancellationToken cancellationToken) =>
        Execute<PayrollPeriodResponse>(async () =>
        {
            var period = await sender.Send(new CreatePayrollPeriodCommand(request), cancellationToken);
            return CreatedAtAction(nameof(Get), new { id = period.Id }, period);
        });

    [HttpPost("{id:long}/start-processing")]
    [Authorize(Policy = "payroll.run.create")]
    public Task<ActionResult<PayrollPeriodResponse>> StartProcessing(
        long id, PayrollPeriodVersionRequest request, CancellationToken cancellationToken) =>
        Execute<PayrollPeriodResponse>(async () =>
        {
            var period = await sender.Send(
                new StartPayrollProcessingCommand(id, request.ExpectedVersion), cancellationToken);
            return Ok(period);
        });

    [HttpPost("{id:long}/lock")]
    [Authorize(Policy = "payroll.period.lock")]
    public Task<ActionResult<PayrollPeriodResponse>> Lock(
        long id, PayrollPeriodLifecycleRequest request, CancellationToken cancellationToken) =>
        Execute<PayrollPeriodResponse>(async () =>
        {
            var period = await sender.Send(
                new LockPayrollPeriodCommand(id, request.ExpectedVersion), cancellationToken);
            return Ok(period);
        });

    [HttpPost("{id:long}/reopen")]
    [Authorize(Policy = "payroll.period.reopen")]
    public Task<ActionResult<PayrollPeriodResponse>> Reopen(
        long id, ReopenPayrollPeriodRequest request, CancellationToken cancellationToken) =>
        Execute<PayrollPeriodResponse>(async () =>
        {
            var period = await sender.Send(new ReopenPayrollPeriodCommand(id, request), cancellationToken);
            return Ok(period);
        });

    [HttpPost("{id:long}/return-to-open")]
    [Authorize(Policy = "payroll.period.reopen")]
    public Task<ActionResult<PayrollPeriodResponse>> ReturnToOpen(
        long id, ReopenPayrollPeriodRequest request, CancellationToken cancellationToken) =>
        Execute<PayrollPeriodResponse>(async () =>
        {
            var period = await sender.Send(
                new ReturnPayrollPeriodToOpenCommand(id, request), cancellationToken);
            return Ok(period);
        });

    [HttpPost("{id:long}/close")]
    [Authorize(Policy = "payroll.period.close")]
    public Task<ActionResult<PayrollPeriodCloseResponse>> Close(
        long id, ClosePayrollPeriodRequest request, CancellationToken cancellationToken) =>
        Execute<PayrollPeriodCloseResponse>(async () =>
        {
            var result = await sender.Send(new ClosePayrollPeriodCommand(id, request), cancellationToken);
            return result.Closed ? Ok(result) : Conflict(result);
        });

    private async Task<ActionResult<T>> Execute<T>(Func<Task<ActionResult<T>>> action)
    {
        try { return await action(); }
        catch (UnauthorizedAccessException exception)
        {
            logger.LogWarning("Payroll period access rejected: {Message}", exception.Message);
            return Problem(statusCode: StatusCodes.Status403Forbidden, title: "Payroll period access denied.");
        }
        catch (ArgumentException exception)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid Payroll period.", detail: exception.Message);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (PayrollPeriodConflictException exception)
        {
            logger.LogWarning("Payroll period conflict: {Message}", exception.Message);
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "Payroll period conflict.", detail: exception.Message);
        }
    }
}

public sealed record PayrollPeriodVersionRequest(long ExpectedVersion);
