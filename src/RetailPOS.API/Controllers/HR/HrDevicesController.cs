using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.Application.HR.Attendance;
using RetailPOS.Application.HR.Calendars;

namespace RetailPOS.API.Controllers.HR;

[ApiController]
[Authorize]
[Route("api/hr/v1/attendance")]
public sealed class HrDevicesController(ISender sender, ILogger<HrDevicesController> logger) : ControllerBase
{
    [HttpGet("devices")]
    [Authorize(Policy = "hr.device.view")]
    public Task<ActionResult<IReadOnlyList<HrDeviceResponse>>> List(CancellationToken ct) =>
        Execute<IReadOnlyList<HrDeviceResponse>>(async () => Ok(await sender.Send(new GetHrDevicesQuery(), ct)));
    [HttpGet("devices/{id:long}")]
    [Authorize(Policy = "hr.device.view")]
    public Task<ActionResult<HrDeviceResponse>> Get(long id, CancellationToken ct) =>
        Execute<HrDeviceResponse>(async () => Ok(await sender.Send(new GetHrDeviceQuery(id), ct)));
    [HttpPost("devices")]
    [Authorize(Policy = "hr.device.manage")]
    public Task<ActionResult<HrDeviceResponse>> Create(HrDeviceData data, CancellationToken ct) =>
        Execute<HrDeviceResponse>(async () => {
            var row = await sender.Send(new SaveHrDeviceCommand(null, null, data), ct);
            return CreatedAtAction(nameof(Get), new { id = row.Id }, row);
        });
    [HttpPut("devices/{id:long}")]
    [Authorize(Policy = "hr.device.manage")]
    public Task<ActionResult<HrDeviceResponse>> Update(long id, HrDeviceUpdateRequest r, CancellationToken ct) =>
        Execute<HrDeviceResponse>(async () => Ok(await sender.Send(new SaveHrDeviceCommand(id, r.ExpectedVersion, r.Data, r.ReplaceCredentialReference), ct)));
    [HttpPost("devices/{id:long}/deactivate")]
    [Authorize(Policy = "hr.device.manage")]
    public Task<ActionResult<HrDeviceResponse>> Deactivate(long id, HrConfigurationVersionRequest r, CancellationToken ct) =>
        Execute<HrDeviceResponse>(async () => Ok(await sender.Send(new DeactivateHrDeviceCommand(id, r.ExpectedVersion), ct)));
    [HttpGet("devices/{id:long}/mappings")]
    [Authorize(Policy = "hr.device_user_mapping.view")]
    public Task<ActionResult<IReadOnlyList<HrDeviceMappingResponse>>> Mappings(long id, CancellationToken ct) =>
        Execute<IReadOnlyList<HrDeviceMappingResponse>>(async () => Ok(await sender.Send(new GetHrDeviceMappingsQuery(id), ct)));
    [HttpPost("devices/{id:long}/mappings")]
    [Authorize(Policy = "hr.device_user_mapping.manage")]
    public Task<ActionResult<HrDeviceMappingResponse>> CreateMapping(long id, HrDeviceMappingData r, CancellationToken ct) =>
        Execute<HrDeviceMappingResponse>(async () => {
            var row = await sender.Send(new CreateHrDeviceMappingCommand(id, r), ct);
            return CreatedAtAction(nameof(Mappings), new { id }, row);
        });
    [HttpDelete("device-user-mappings/{id:long}")]
    [Authorize(Policy = "hr.device_user_mapping.manage")]
    public Task<ActionResult<HrDeviceMappingResponse>> CloseMapping(long id, HrDeviceMappingCloseRequest r, CancellationToken ct) =>
        Execute<HrDeviceMappingResponse>(async () => Ok(await sender.Send(new CloseHrDeviceMappingCommand(id, r.ExpectedVersion, r.EffectiveToUtc), ct)));
    private async Task<ActionResult<T>> Execute<T>(Func<Task<ActionResult<T>>> action)
    {
        try { return await action(); }
        catch (UnauthorizedAccessException e) { logger.LogWarning("Device access rejected: {Message}", e.Message); return Problem(statusCode: 403, title: "Device access denied.", detail: e.Message); }
        catch (KeyNotFoundException e) { return Problem(statusCode: 404, title: "Device reference not found.", detail: e.Message); }
        catch (ArgumentException e) { return Problem(statusCode: 400, title: "Invalid device configuration.", detail: e.Message); }
        catch (HrCalendarConflictException e) { logger.LogWarning("Device configuration conflict: {Message}", e.Message); return Problem(statusCode: 409, title: "Device configuration conflict.", detail: e.Message); }
    }
}
/// <summary>Device update. ReplaceCredentialReference explicitly replaces or clears the secret name.</summary>
public sealed record HrDeviceUpdateRequest(long ExpectedVersion, HrDeviceData Data, bool ReplaceCredentialReference = false);
/// <summary>Closes a mapping's half-open UTC interval without deleting its history.</summary>
public sealed record HrDeviceMappingCloseRequest(long ExpectedVersion, DateTimeOffset EffectiveToUtc);
