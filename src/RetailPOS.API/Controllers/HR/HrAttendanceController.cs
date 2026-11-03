using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.Application.HR.Approvals;
using RetailPOS.Application.HR.Attendance;
using RetailPOS.Core.Entities.HR.Approvals;

namespace RetailPOS.API.Controllers.HR;

[ApiController]
[Authorize]
[Route("api/hr/v1/attendance")]
public sealed class HrAttendanceController(ISender sender, ILogger<HrAttendanceController> logger) : ControllerBase
{
    [HttpPost("process")]
    [Authorize(Policy = "hr.attendance.reprocess")]
    public Task<ActionResult<IReadOnlyList<HrDailyAttendanceResponse>>> Process(HrAttendanceRangeRequest request,
        CancellationToken ct) =>
        Execute<IReadOnlyList<HrDailyAttendanceResponse>>(async () =>
            Ok(await sender.Send(new ProcessHrAttendanceCommand(request.From, request.To, request.EmployeeId), ct)));

    [HttpGet("daily")]
    [Authorize(Policy = "hr.attendance.view")]
    public Task<ActionResult<IReadOnlyList<HrDailyAttendanceResponse>>> List([FromQuery] DateOnly from,
        [FromQuery] DateOnly to, [FromQuery] long? employeeId, CancellationToken ct) =>
        Execute<IReadOnlyList<HrDailyAttendanceResponse>>(async () =>
            Ok(await sender.Send(new GetHrDailyAttendanceQuery(from, to, employeeId), ct)));

    /// <summary>Returns paged, scope-limited attendance with frozen shift and calendar evidence.</summary>
    [HttpGet("reports")]
    [Authorize(Policy = "hr.attendance.report")]
    [ProducesResponseType(typeof(HrAttendanceReportPage), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public Task<ActionResult<HrAttendanceReportPage>> Report(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] long? employeeId,
        [FromQuery] long? locationProjectionId,
        [FromQuery] string? status,
        [FromQuery] bool exceptionsOnly,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default) =>
        Execute<HrAttendanceReportPage>(async () =>
            Ok(await sender.Send(new HrAttendanceReportQuery(from, to, employeeId,
                locationProjectionId, status, exceptionsOnly, page, pageSize), ct)));

    /// <summary>Returns retained correction evidence for one daily attendance record.</summary>
    [HttpGet("daily/{dailyId:long}/corrections")]
    [Authorize(Policy = "hr.attendance.view")]
    [ProducesResponseType(typeof(IReadOnlyList<HrAttendanceCorrectionResponse>), StatusCodes.Status200OK)]
    public Task<ActionResult<IReadOnlyList<HrAttendanceCorrectionResponse>>> Corrections(
        long dailyId, CancellationToken ct) =>
        Execute<IReadOnlyList<HrAttendanceCorrectionResponse>>(async () =>
            Ok(await sender.Send(new GetHrAttendanceCorrectionsQuery(dailyId), ct)));

    /// <summary>Submits an explicit attendance correction to the Approval Kernel.</summary>
    [HttpPost("daily/{dailyId:long}/corrections")]
    [Authorize(Policy = "hr.attendance.correct")]
    [ProducesResponseType(typeof(HrAttendanceCorrectionSubmissionResult), StatusCodes.Status202Accepted)]
    public Task<ActionResult<HrAttendanceCorrectionSubmissionResult>> RequestCorrection(
        long dailyId, [FromBody] RequestHrAttendanceCorrectionRequest request, CancellationToken ct) =>
        Execute<HrAttendanceCorrectionSubmissionResult>(async () =>
        {
            var result = await sender.Send(new RequestHrAttendanceCorrectionCommand(
                request.CorrectionId, dailyId, request.ExpectedProcessingVersion,
                new HrAttendanceCorrectionValues(request.FirstInAtLocal, request.LastOutAtLocal,
                    request.WorkedMinutes, request.BreakMinutes, request.LateMinutes,
                    request.EarlyLeaveMinutes, request.Status), request.Reason), ct);
            return Accepted($"/api/hr/v1/attendance/daily/{dailyId}/corrections", result);
        });

    /// <summary>Approves a pending attendance correction.</summary>
    [HttpPost("corrections/approvals/{instanceId:long}/approve")]
    [Authorize(Policy = "hr.attendance.approve_correction")]
    [ProducesResponseType(typeof(ApprovalDecisionResult), StatusCodes.Status200OK)]
    public Task<ActionResult<ApprovalDecisionResult>> ApproveCorrection(
        long instanceId, [FromBody] DecideAttendanceCorrectionRequest request, CancellationToken ct) =>
        DecideCorrection(instanceId, request, ApprovalDecisionType.Approve, ct);

    /// <summary>Rejects a pending attendance correction while retaining its evidence.</summary>
    [HttpPost("corrections/approvals/{instanceId:long}/reject")]
    [Authorize(Policy = "hr.attendance.approve_correction")]
    [ProducesResponseType(typeof(ApprovalDecisionResult), StatusCodes.Status200OK)]
    public Task<ActionResult<ApprovalDecisionResult>> RejectCorrection(
        long instanceId, [FromBody] DecideAttendanceCorrectionRequest request, CancellationToken ct) =>
        DecideCorrection(instanceId, request, ApprovalDecisionType.Reject, ct);

    /// <summary>Cancels a pending attendance correction approval request.</summary>
    [HttpPost("corrections/approvals/{instanceId:long}/cancel")]
    [Authorize(Policy = "hr.attendance.correct")]
    [ProducesResponseType(typeof(ApprovalDecisionResult), StatusCodes.Status200OK)]
    public Task<ActionResult<ApprovalDecisionResult>> CancelCorrection(
        long instanceId, [FromBody] CancelAttendanceCorrectionRequest request, CancellationToken ct) =>
        Execute<ApprovalDecisionResult>(async () =>
            Ok(await sender.Send(new CancelApprovalCommand(instanceId, request.DecisionKey, request.Reason), ct)));

    private Task<ActionResult<ApprovalDecisionResult>> DecideCorrection(
        long instanceId, DecideAttendanceCorrectionRequest request, ApprovalDecisionType decision, CancellationToken ct) =>
        Execute<ApprovalDecisionResult>(async () =>
            Ok(await sender.Send(new RecordApprovalDecisionCommand(
                instanceId, request.DecisionKey, decision, request.Reason), ct)));

    private async Task<ActionResult<T>> Execute<T>(Func<Task<ActionResult<T>>> action)
    {
        try { return await action(); }
        catch (UnauthorizedAccessException e)
        {
            logger.LogWarning("Attendance access rejected: {Message}", e.Message);
            return Problem(statusCode: 403, title: "Attendance access denied.", detail: e.Message);
        }
        catch (KeyNotFoundException e) { return Problem(statusCode: 404, title: "Attendance reference not found.", detail: e.Message); }
        catch (ArgumentException e) { return Problem(statusCode: 400, title: "Invalid attendance request.", detail: e.Message); }
        catch (InvalidOperationException e)
        {
            logger.LogWarning("Attendance processing conflict: {Message}", e.Message);
            return Problem(statusCode: 409, title: "Attendance processing conflict.", detail: e.Message);
        }
    }
}

/// <summary>Range filter used to process published attendance rosters.</summary>
public sealed record HrAttendanceRangeRequest(DateOnly From, DateOnly To, long? EmployeeId = null);

/// <summary>Explicit replacement values for a correction request; timestamps are local wall-clock values.</summary>
public sealed record RequestHrAttendanceCorrectionRequest
{
    /// <summary>Stable client-generated idempotency key for this correction request.</summary>
    public required Guid CorrectionId { get; init; }
    /// <summary>Attendance processing version the correction was prepared against.</summary>
    public required int ExpectedProcessingVersion { get; init; }
    /// <summary>Corrected local wall-clock first-in timestamp.</summary>
    public DateTime? FirstInAtLocal { get; init; }
    /// <summary>Corrected local wall-clock last-out timestamp.</summary>
    public DateTime? LastOutAtLocal { get; init; }
    /// <summary>Corrected worked minutes.</summary>
    public required int WorkedMinutes { get; init; }
    /// <summary>Corrected break minutes.</summary>
    public required int BreakMinutes { get; init; }
    /// <summary>Corrected late minutes.</summary>
    public required int LateMinutes { get; init; }
    /// <summary>Corrected early-leave minutes.</summary>
    public required int EarlyLeaveMinutes { get; init; }
    /// <summary>Corrected attendance status.</summary>
    public required string Status { get; init; }
    /// <summary>Business reason for the requested correction.</summary>
    public required string Reason { get; init; }
}

/// <summary>Idempotency key and reason for an approval decision.</summary>
public sealed record DecideAttendanceCorrectionRequest
{
    /// <summary>Stable key for replay-safe decision submission.</summary>
    public required string DecisionKey { get; init; }
    /// <summary>Optional decision comment; required for rejection.</summary>
    public string? Reason { get; init; }
}

/// <summary>Idempotency key and reason for cancelling a pending correction approval.</summary>
public sealed record CancelAttendanceCorrectionRequest
{
    /// <summary>Stable key for replay-safe cancellation.</summary>
    public required string DecisionKey { get; init; }
    /// <summary>Required cancellation reason.</summary>
    public required string Reason { get; init; }
}
