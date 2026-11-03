using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.Application.HR.Attendance;

namespace RetailPOS.API.Controllers.HR;

[ApiController]
[Authorize]
[Route("api/hr/v1/attendance")]
public sealed class HrRawAttendancePunchesController(ISender sender, ILogger<HrRawAttendancePunchesController> logger) : ControllerBase
{
    [HttpPost("manual-punches")]
    [Authorize(Policy = "hr.attendance.manual_entry")]
    public Task<ActionResult<RawAttendancePunchResponse>> AddManual(ManualAttendancePunchInput input, CancellationToken ct) =>
        Execute<RawAttendancePunchResponse>(async () =>
        {
            var result = await sender.Send(new AddManualAttendancePunchCommand(input), ct);
            return result.Duplicate ? Ok(result) : CreatedAtAction(nameof(List),
                new { fromUtc = result.ReceivedAtUtc.AddMinutes(-1), toUtc = result.ReceivedAtUtc.AddMinutes(1) }, result);
        });

    [HttpGet("raw-punches")]
    [Authorize(Policy = "hr.attendance.raw_punch.view")]
    public Task<ActionResult<IReadOnlyList<RawAttendancePunchListItem>>> List(
        [FromQuery] DateTimeOffset fromUtc, [FromQuery] DateTimeOffset toUtc, CancellationToken ct) =>
        Execute<IReadOnlyList<RawAttendancePunchListItem>>(async () =>
            Ok(await sender.Send(new GetRawAttendancePunchesQuery(fromUtc, toUtc), ct)));

    private async Task<ActionResult<T>> Execute<T>(Func<Task<ActionResult<T>>> action)
    {
        try { return await action(); }
        catch (UnauthorizedAccessException e)
        {
            logger.LogWarning("Attendance punch access rejected: {Message}", e.Message);
            return Problem(statusCode: 403, title: "Attendance punch access denied.", detail: e.Message);
        }
        catch (KeyNotFoundException e) { return Problem(statusCode: 404, title: "Attendance reference not found.", detail: e.Message); }
        catch (ArgumentException e) { return Problem(statusCode: 400, title: "Invalid attendance punch.", detail: e.Message); }
    }
}
