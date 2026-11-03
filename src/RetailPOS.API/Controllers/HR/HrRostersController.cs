using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.Application.HR.Attendance;
using RetailPOS.Application.HR.Calendars;

namespace RetailPOS.API.Controllers.HR;

[ApiController]
[Authorize]
[Route("api/hr/v1/rosters")]
public sealed class HrRostersController(ISender sender, ILogger<HrRostersController> logger) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = "hr.roster.view")]
    public Task<ActionResult<IReadOnlyList<HrRosterResponse>>> List([FromQuery] DateOnly from, [FromQuery] DateOnly to,
        [FromQuery] long? employeeId, CancellationToken ct) =>
        Execute<IReadOnlyList<HrRosterResponse>>(async () => Ok(await sender.Send(new GetHrRostersQuery(from, to, employeeId), ct)));

    [HttpPost]
    [Authorize(Policy = "hr.roster.manage")]
    public Task<ActionResult<HrRosterResponse>> Create(HrRosterData data, CancellationToken ct) =>
        Execute<HrRosterResponse>(async () => {
            var row = await sender.Send(new SaveHrRosterCommand(null, null, data), ct);
            return CreatedAtAction(nameof(List), new { from = data.WorkDate, to = data.WorkDate, employeeId = data.EmployeeId }, row);
        });

    [HttpPut("{id:long}")]
    [Authorize(Policy = "hr.roster.manage")]
    public Task<ActionResult<HrRosterResponse>> Update(long id, HrRosterUpdateRequest request, CancellationToken ct) =>
        Execute<HrRosterResponse>(async () => Ok(await sender.Send(new SaveHrRosterCommand(id, request.ExpectedVersion, request.Data), ct)));

    [HttpPost("bulk")]
    [Authorize(Policy = "hr.roster.manage")]
    public Task<ActionResult<IReadOnlyList<HrRosterResponse>>> Bulk(HrRosterBulkRequest request, CancellationToken ct) =>
        Execute<IReadOnlyList<HrRosterResponse>>(async () => {
            var rows = await sender.Send(new BulkAssignHrRosterCommand(request.Rows), ct);
            return StatusCode(201, rows);
        });

    [HttpPost("{id:long}/publish")]
    [Authorize(Policy = "hr.roster.manage")]
    public Task<ActionResult<HrRosterResponse>> Publish(long id, HrConfigurationVersionRequest request, CancellationToken ct) =>
        Execute<HrRosterResponse>(async () => Ok(await sender.Send(new TransitionHrRosterCommand(id, request.ExpectedVersion, "Published"), ct)));

    [HttpPost("{id:long}/cancel")]
    [Authorize(Policy = "hr.roster.manage")]
    public Task<ActionResult<HrRosterResponse>> Cancel(long id, HrConfigurationVersionRequest request, CancellationToken ct) =>
        Execute<HrRosterResponse>(async () => Ok(await sender.Send(new TransitionHrRosterCommand(id, request.ExpectedVersion, "Cancelled"), ct)));

    [HttpGet("classification")]
    [Authorize(Policy = "hr.roster.view")]
    public Task<ActionResult<HrDayClassificationResponse>> Classify([FromQuery] long employeeId, [FromQuery] DateOnly workDate, CancellationToken ct) =>
        Execute<HrDayClassificationResponse>(async () => Ok(await sender.Send(new GetHrRosterClassificationQuery(employeeId, workDate), ct)));

    private async Task<ActionResult<T>> Execute<T>(Func<Task<ActionResult<T>>> action)
    {
        try { return await action(); }
        catch (UnauthorizedAccessException e) { logger.LogWarning("Roster access rejected: {Message}", e.Message); return Problem(statusCode: 403, title: "Roster access denied.", detail: e.Message); }
        catch (KeyNotFoundException e) { return Problem(statusCode: 404, title: "Roster reference not found.", detail: e.Message); }
        catch (ArgumentException e) { return Problem(statusCode: 400, title: "Invalid roster.", detail: e.Message); }
        catch (HrCalendarConflictException e) { logger.LogWarning("Roster conflict: {Message}", e.Message); return Problem(statusCode: 409, title: "Roster conflict.", detail: e.Message); }
        catch (InvalidOperationException e) when (e.Message == "An inactive shift cannot be newly assigned.")
        { return Problem(statusCode: 400, title: "Inactive shift.", detail: e.Message); }
    }
}

/// <summary>Draft roster replacement with expected optimistic version.</summary>
public sealed record HrRosterUpdateRequest(long ExpectedVersion, HrRosterData Data);
/// <summary>Atomic draft assignment of up to 100 employee/date rows.</summary>
public sealed record HrRosterBulkRequest(IReadOnlyList<HrRosterData> Rows);
