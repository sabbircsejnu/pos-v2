using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.Application.HR.Calendars;

namespace RetailPOS.API.Controllers.HR;

[ApiController]
[Authorize]
[Route("api/hr/v1")]
public sealed class HrCalendarsController(ISender sender, ILogger<HrCalendarsController> logger) : ControllerBase
{
    [HttpGet("shifts")]
    [Authorize(Policy = "hr.shift.view")]
    public Task<ActionResult<IReadOnlyList<HrShiftResponse>>> Shifts([FromQuery] string? search, CancellationToken ct) =>
        Execute<IReadOnlyList<HrShiftResponse>>(async () => Ok(await sender.Send(new GetHrShiftsQuery(search), ct)));

    [HttpPost("shifts")]
    [Authorize(Policy = "hr.shift.manage")]
    public Task<ActionResult<HrShiftResponse>> CreateShift(HrShiftData data, CancellationToken ct) =>
        Execute<HrShiftResponse>(async () => {
            var row = await sender.Send(new SaveHrShiftCommand(null, null, data), ct);
            return CreatedAtAction(nameof(Shifts), new { id = row.Id }, row);
        });

    [HttpPut("shifts/{id:long}")]
    [Authorize(Policy = "hr.shift.manage")]
    public Task<ActionResult<HrShiftResponse>> UpdateShift(long id, HrShiftUpdateRequest request, CancellationToken ct) =>
        Execute<HrShiftResponse>(async () => Ok(await sender.Send(new SaveHrShiftCommand(id, request.ExpectedVersion, request.Data), ct)));

    [HttpPost("shifts/{id:long}/deactivate")]
    [Authorize(Policy = "hr.shift.manage")]
    public Task<ActionResult<HrShiftResponse>> DeactivateShift(long id, HrConfigurationVersionRequest request, CancellationToken ct) =>
        Execute<HrShiftResponse>(async () => Ok(await sender.Send(new SaveHrShiftCommand(id, request.ExpectedVersion, null), ct)));

    [HttpGet("holiday-calendars")]
    [Authorize(Policy = "hr.holiday.view")]
    public Task<ActionResult<IReadOnlyList<HrHolidayCalendarResponse>>> Calendars(CancellationToken ct) =>
        Execute<IReadOnlyList<HrHolidayCalendarResponse>>(async () => Ok(await sender.Send(new GetHrHolidayCalendarsQuery(), ct)));

    [HttpPost("holiday-calendars")]
    [Authorize(Policy = "hr.holiday.manage")]
    public Task<ActionResult<HrHolidayCalendarResponse>> CreateCalendar(HrHolidayCalendarData data, CancellationToken ct) =>
        Execute<HrHolidayCalendarResponse>(async () => {
            var row = await sender.Send(new SaveHrHolidayCalendarCommand(null, null, data), ct);
            return CreatedAtAction(nameof(Calendars), new { id = row.Id }, row);
        });

    [HttpPut("holiday-calendars/{id:long}")]
    [Authorize(Policy = "hr.holiday.manage")]
    public Task<ActionResult<HrHolidayCalendarResponse>> UpdateCalendar(long id, HrCalendarUpdateRequest request, CancellationToken ct) =>
        Execute<HrHolidayCalendarResponse>(async () => Ok(await sender.Send(
            new SaveHrHolidayCalendarCommand(id, request.ExpectedVersion, request.Data), ct)));

    [HttpPost("holiday-calendars/{id:long}/deactivate")]
    [Authorize(Policy = "hr.holiday.manage")]
    public Task<ActionResult<HrHolidayCalendarResponse>> DeactivateCalendar(long id, HrConfigurationEndRequest request, CancellationToken ct) =>
        Execute<HrHolidayCalendarResponse>(async () => Ok(await sender.Send(
            new SaveHrHolidayCalendarCommand(id, request.ExpectedVersion, null, request.EffectiveTo), ct)));

    [HttpGet("holiday-calendars/{calendarId:long}/holidays")]
    [Authorize(Policy = "hr.holiday.view")]
    public Task<ActionResult<IReadOnlyList<HrHolidayResponse>>> Holidays(long calendarId, CancellationToken ct) =>
        Execute<IReadOnlyList<HrHolidayResponse>>(async () => Ok(await sender.Send(new GetHrHolidaysQuery(calendarId), ct)));

    [HttpPost("holiday-calendars/{calendarId:long}/holidays")]
    [Authorize(Policy = "hr.holiday.manage")]
    public Task<ActionResult<HrHolidayResponse>> CreateHoliday(long calendarId, HrHolidayData data, CancellationToken ct) =>
        Execute<HrHolidayResponse>(async () => {
            var row = await sender.Send(new SaveHrHolidayCommand(calendarId, null, null, data), ct);
            return CreatedAtAction(nameof(Holidays), new { calendarId, id = row.Id }, row);
        });

    [HttpPut("holiday-calendars/{calendarId:long}/holidays/{id:long}")]
    [Authorize(Policy = "hr.holiday.manage")]
    public Task<ActionResult<HrHolidayResponse>> UpdateHoliday(long calendarId, long id, HrHolidayUpdateRequest request, CancellationToken ct) =>
        Execute<HrHolidayResponse>(async () => Ok(await sender.Send(
            new SaveHrHolidayCommand(calendarId, id, request.ExpectedVersion, request.Data), ct)));

    [HttpPost("holiday-calendars/{calendarId:long}/holidays/{id:long}/deactivate")]
    [Authorize(Policy = "hr.holiday.manage")]
    public Task<ActionResult<HrHolidayResponse>> DeactivateHoliday(long calendarId, long id, HrConfigurationVersionRequest request, CancellationToken ct) =>
        Execute<HrHolidayResponse>(async () => Ok(await sender.Send(
            new SaveHrHolidayCommand(calendarId, id, request.ExpectedVersion, null), ct)));

    [HttpGet("weekly-off-policies")]
    [Authorize(Policy = "hr.weekly_off.view")]
    public Task<ActionResult<IReadOnlyList<HrWeeklyOffResponse>>> WeeklyOff(CancellationToken ct) =>
        Execute<IReadOnlyList<HrWeeklyOffResponse>>(async () => Ok(await sender.Send(new GetHrWeeklyOffQuery(), ct)));

    [HttpPost("weekly-off-policies")]
    [Authorize(Policy = "hr.weekly_off.manage")]
    public Task<ActionResult<HrWeeklyOffResponse>> CreateWeeklyOff(HrWeeklyOffData data, CancellationToken ct) =>
        Execute<HrWeeklyOffResponse>(async () => {
            var row = await sender.Send(new SaveHrWeeklyOffCommand(null, null, data), ct);
            return CreatedAtAction(nameof(WeeklyOff), new { id = row.Id }, row);
        });

    [HttpPost("weekly-off-policies/{id:long}/versions")]
    [HttpPut("weekly-off-policies/{id:long}")]
    [Authorize(Policy = "hr.weekly_off.manage")]
    public Task<ActionResult<HrWeeklyOffResponse>> VersionWeeklyOff(long id, HrWeeklyOffVersionRequest request, CancellationToken ct) =>
        Execute<HrWeeklyOffResponse>(async () => Ok(await sender.Send(
            new SaveHrWeeklyOffCommand(id, request.ExpectedVersion, request.Data), ct)));

    [HttpPost("weekly-off-policies/{id:long}/deactivate")]
    [Authorize(Policy = "hr.weekly_off.manage")]
    public Task<ActionResult<HrWeeklyOffResponse>> DeactivateWeeklyOff(long id, HrConfigurationEndRequest request, CancellationToken ct) =>
        Execute<HrWeeklyOffResponse>(async () => Ok(await sender.Send(
            new SaveHrWeeklyOffCommand(id, request.ExpectedVersion, null, request.EffectiveTo), ct)));

    [HttpGet("day-classification")]
    [Authorize(Policy = "hr.holiday.view")]
    [Authorize(Policy = "hr.weekly_off.view")]
    public Task<ActionResult<HrDayClassificationResponse>> Classify([FromQuery] DateOnly workDate,
        [FromQuery] long locationId, CancellationToken ct) =>
        Execute<HrDayClassificationResponse>(async () => Ok(await sender.Send(new GetHrDayClassificationQuery(workDate, locationId), ct)));

    private async Task<ActionResult<T>> Execute<T>(Func<Task<ActionResult<T>>> action)
    {
        try { return await action(); }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning("HR calendar access rejected: {Message}", ex.Message);
            return Problem(statusCode: 403, title: "Calendar access denied.", detail: ex.Message);
        }
        catch (ArgumentException ex) { return Problem(statusCode: 400, title: "Invalid calendar configuration.", detail: ex.Message); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (HrCalendarConflictException ex)
        {
            logger.LogWarning("HR calendar conflict: {Message}", ex.Message);
            return Problem(statusCode: 409, title: "Calendar configuration conflict.", detail: ex.Message);
        }
    }
}

/// <summary>An expected optimistic concurrency version.</summary>
public sealed record HrConfigurationVersionRequest(long ExpectedVersion);
/// <summary>A future effective end and expected optimistic concurrency version.</summary>
public sealed record HrConfigurationEndRequest(long ExpectedVersion, DateOnly EffectiveTo);
/// <summary>A shift replacement and expected version.</summary>
public sealed record HrShiftUpdateRequest(long ExpectedVersion, HrShiftData Data);
/// <summary>A future calendar replacement and expected version.</summary>
public sealed record HrCalendarUpdateRequest(long ExpectedVersion, HrHolidayCalendarData Data);
/// <summary>A future holiday replacement and expected version.</summary>
public sealed record HrHolidayUpdateRequest(long ExpectedVersion, HrHolidayData Data);
/// <summary>An append-only weekly-off successor and expected predecessor version.</summary>
public sealed record HrWeeklyOffVersionRequest(long ExpectedVersion, HrWeeklyOffData Data);
