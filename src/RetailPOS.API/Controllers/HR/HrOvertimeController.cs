using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.Application.HR.Approvals;
using RetailPOS.Application.HR.Overtime;
using RetailPOS.Core.Entities.HR.Approvals;
using RetailPOS.Core.Entities.HR.Overtime;

namespace RetailPOS.API.Controllers.HR;

[ApiController]
[Authorize]
[Route("api/hr/v1/overtime")]
public sealed class HrOvertimeController(ISender sender, ILogger<HrOvertimeController> logger) : ControllerBase
{
    [HttpGet("policies")]
    [Authorize(Policy = "hr.overtime.view")]
    [ProducesResponseType(typeof(IReadOnlyList<HrOvertimePolicyModel>), StatusCodes.Status200OK)]
    public Task<ActionResult<IReadOnlyList<HrOvertimePolicyModel>>> Policies(CancellationToken ct) =>
        Execute<IReadOnlyList<HrOvertimePolicyModel>>(async () =>
            Ok(await sender.Send(new GetHrOvertimePoliciesQuery(), ct)));

    [HttpPost("policies")]
    [Authorize(Policy = "hr.overtime_policy.manage")]
    [ProducesResponseType(typeof(HrOvertimePolicyModel), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<ActionResult<HrOvertimePolicyModel>> CreatePolicy(
        [FromBody] HrOvertimePolicyRequest request, CancellationToken ct) =>
        Execute<HrOvertimePolicyModel>(async () =>
        {
            var input = new HrOvertimePolicyInput(
                request.Code, request.Name, request.ScopeType, request.ScopeReference,
                request.VersionNo, request.EffectiveFrom, request.EffectiveTo,
                request.MinimumEligibleMinutes, request.MaximumEligibleMinutes,
                request.RoundingUnitMinutes, request.RoundingMethod,
                request.IncludePreShift, request.IncludePostShift,
                request.AllowNormalWorkingDay, request.AllowWeeklyOff,
                request.AllowHoliday, request.AllowSpecialDay,
                request.RateType, request.NormalDayRate, request.WeeklyOffRate,
                request.HolidayRate, request.SpecialDayRate, request.IsActive);
            var created = await sender.Send(new CreateHrOvertimePolicyCommand(input), ct);
            return CreatedAtAction(nameof(Policies), new { }, created);
        });

    [HttpPost("calculate")]
    [Authorize(Policy = "hr.overtime.calculate")]
    [ProducesResponseType(typeof(IReadOnlyList<HrOvertimeModel>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<ActionResult<IReadOnlyList<HrOvertimeModel>>> Calculate(
        [FromBody] HrOvertimeRangeRequest request, CancellationToken ct) =>
        Execute<IReadOnlyList<HrOvertimeModel>>(async () =>
            Ok(await sender.Send(new CalculateHrOvertimeCommand(
                request.From, request.To, request.EmployeeId, request.LocationProjectionId), ct)));

    [HttpGet("entries")]
    [Authorize(Policy = "hr.overtime.view")]
    [ProducesResponseType(typeof(IReadOnlyList<HrOvertimeModel>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public Task<ActionResult<IReadOnlyList<HrOvertimeModel>>> List(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] long? employeeId,
        [FromQuery] long? locationProjectionId,
        [FromQuery] string? status,
        CancellationToken ct) =>
        Execute<IReadOnlyList<HrOvertimeModel>>(async () =>
            Ok(await sender.Send(new GetHrOvertimeQuery(
                from, to, employeeId, locationProjectionId, status), ct)));

    [HttpGet("entries/{id:long}", Name = nameof(GetEntry))]
    [Authorize(Policy = "hr.overtime.view")]
    [ProducesResponseType(typeof(HrOvertimeModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public Task<ActionResult<HrOvertimeModel>> GetEntry(long id, CancellationToken ct) =>
        Execute<HrOvertimeModel>(async () =>
        {
            var entry = await sender.Send(new GetHrOvertimeEntryQuery(id), ct);
            return entry is null ? NotFound() : Ok(entry);
        });

    [HttpPost("entries/{id:long}/submit")]
    [Authorize(Policy = "hr.overtime.submit")]
    [ProducesResponseType(typeof(ApprovalDecisionResult), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<ActionResult<ApprovalDecisionResult>> Submit(
        long id, [FromBody] HrOvertimeSubmitRequest request, CancellationToken ct) =>
        Execute<ApprovalDecisionResult>(async () =>
            Accepted($"/api/hr/v1/overtime/entries/{id}",
                await sender.Send(new SubmitHrOvertimeCommand(id, request.ExpectedVersion), ct)));

    [HttpPost("entries/{id:long}/cancel")]
    [Authorize(Policy = "hr.overtime.submit")]
    [ProducesResponseType(typeof(ApprovalDecisionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<ActionResult<ApprovalDecisionResult>> Cancel(
        long id, [FromBody] HrOvertimeCancelRequest request, CancellationToken ct) =>
        Execute<ApprovalDecisionResult>(async () =>
            Ok(await sender.Send(new CancelHrOvertimeCommand(id, request.DecisionKey, request.Reason), ct)));

    [HttpPost("approvals/{instanceId:long}/approve")]
    [Authorize(Policy = "hr.overtime.approve")]
    [ProducesResponseType(typeof(ApprovalDecisionResult), StatusCodes.Status200OK)]
    public Task<ActionResult<ApprovalDecisionResult>> Approve(
        long instanceId, [FromBody] HrOvertimeDecisionRequest request, CancellationToken ct) =>
        Decide(instanceId, request, ApprovalDecisionType.Approve, ct);

    [HttpPost("approvals/{instanceId:long}/reject")]
    [Authorize(Policy = "hr.overtime.approve")]
    [ProducesResponseType(typeof(ApprovalDecisionResult), StatusCodes.Status200OK)]
    public Task<ActionResult<ApprovalDecisionResult>> Reject(
        long instanceId, [FromBody] HrOvertimeDecisionRequest request, CancellationToken ct) =>
        Decide(instanceId, request, ApprovalDecisionType.Reject, ct);

    private Task<ActionResult<ApprovalDecisionResult>> Decide(
        long instanceId, HrOvertimeDecisionRequest request, ApprovalDecisionType decision, CancellationToken ct) =>
        Execute<ApprovalDecisionResult>(async () =>
            Ok(await sender.Send(new RecordApprovalDecisionCommand(
                instanceId, request.DecisionKey, decision, request.Reason), ct)));

    private async Task<ActionResult<T>> Execute<T>(Func<Task<ActionResult<T>>> action)
    {
        try
        {
            return await action();
        }
        catch (UnauthorizedAccessException exception)
        {
            logger.LogWarning("Overtime access rejected: {Message}", exception.Message);
            return Problem(statusCode: StatusCodes.Status403Forbidden,
                title: "Overtime access denied.", detail: exception.Message);
        }
        catch (KeyNotFoundException exception)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound,
                title: "Overtime reference not found.", detail: exception.Message);
        }
        catch (ArgumentException exception)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid overtime request.", detail: exception.Message);
        }
        catch (HrOvertimeConcurrencyException exception)
        {
            logger.LogWarning(exception, "Overtime data changed concurrently.");
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "Overtime operation conflict.", detail: exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            logger.LogWarning("Overtime operation conflict: {Message}", exception.Message);
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "Overtime operation conflict.", detail: exception.Message);
        }
    }
}

/// <summary>Effective-dated overtime policy fields for a new immutable policy version.</summary>
public sealed record HrOvertimePolicyRequest(
    string Code,
    string Name,
    HrOvertimePolicyScopeType ScopeType,
    string? ScopeReference,
    int VersionNo,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    int MinimumEligibleMinutes,
    int? MaximumEligibleMinutes,
    int RoundingUnitMinutes,
    HrOvertimeRoundingMethod RoundingMethod,
    bool IncludePreShift,
    bool IncludePostShift,
    bool AllowNormalWorkingDay,
    bool AllowWeeklyOff,
    bool AllowHoliday,
    bool AllowSpecialDay,
    HrOvertimeRateType RateType,
    decimal NormalDayRate,
    decimal WeeklyOffRate,
    decimal HolidayRate,
    decimal SpecialDayRate,
    bool IsActive);

/// <summary>Filter values for a bounded overtime calculation run.</summary>
public sealed record HrOvertimeRangeRequest(
    DateOnly From,
    DateOnly To,
    long? EmployeeId = null,
    long? LocationProjectionId = null);

/// <summary>Expected version required when submitting calculated overtime.</summary>
public sealed record HrOvertimeSubmitRequest(long ExpectedVersion);

/// <summary>Idempotency key and reason for cancelling a pending overtime approval.</summary>
public sealed record HrOvertimeCancelRequest(string DecisionKey, string Reason);

/// <summary>Decision key and reason for an overtime approval decision.</summary>
public sealed record HrOvertimeDecisionRequest(string DecisionKey, string? Reason);
