using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.Application.HR.Approvals;
using RetailPOS.Application.HR.Payroll;
using RetailPOS.Core.Entities.HR.Approvals;

namespace RetailPOS.API.Controllers.Payroll;

/// <summary>Manages configurable Payroll salary components and reusable salary templates.</summary>
[ApiController]
[Authorize]
[Route("api/payroll/v1")]
public sealed class PayrollSalarySetupController(
    ISender sender,
    ILogger<PayrollSalarySetupController> logger) : ControllerBase
{
    /// <summary>Lists salary components for the validated Business.</summary>
    [HttpGet("salary-components")]
    [Authorize(Policy = "payroll.salary.view")]
    public Task<ActionResult<IReadOnlyList<PayrollSalaryComponentResponse>>> Components(
        [FromQuery] string? search, CancellationToken cancellationToken) =>
        Execute<IReadOnlyList<PayrollSalaryComponentResponse>>(async () => Ok(await sender.Send(
            new GetPayrollSalaryComponentsQuery(search), cancellationToken)));

    /// <summary>Gets one salary component without disclosing other Businesses.</summary>
    [HttpGet("salary-components/{id:long}")]
    [Authorize(Policy = "payroll.salary.view")]
    public Task<ActionResult<PayrollSalaryComponentResponse>> Component(
        long id, CancellationToken cancellationToken) =>
        Execute<PayrollSalaryComponentResponse>(async () => Ok(await sender.Send(
            new GetPayrollSalaryComponentQuery(id), cancellationToken)));

    /// <summary>Creates a configurable earning or deduction component.</summary>
    [HttpPost("salary-components")]
    [Authorize(Policy = "payroll.salary.manage")]
    public Task<ActionResult<PayrollSalaryComponentResponse>> CreateComponent(
        PayrollSalaryComponentData data, CancellationToken cancellationToken) =>
        Execute<PayrollSalaryComponentResponse>(async () =>
        {
            var row = await sender.Send(
                new SavePayrollSalaryComponentCommand(null, null, data), cancellationToken);
            return CreatedAtAction(nameof(Component), new { id = row.Id }, row);
        });

    /// <summary>Updates a salary component using its expected concurrency version.</summary>
    [HttpPut("salary-components/{id:long}")]
    [Authorize(Policy = "payroll.salary.manage")]
    public Task<ActionResult<PayrollSalaryComponentResponse>> UpdateComponent(
        long id, PayrollSalaryComponentUpdateRequest request, CancellationToken cancellationToken) =>
        Execute<PayrollSalaryComponentResponse>(async () => Ok(await sender.Send(
            new SavePayrollSalaryComponentCommand(id, request.ExpectedVersion, request.Data),
            cancellationToken)));

    /// <summary>Deactivates a component that is not needed by an active structure.</summary>
    [HttpPost("salary-components/{id:long}/deactivate")]
    [Authorize(Policy = "payroll.salary.manage")]
    public Task<ActionResult<PayrollSalaryComponentResponse>> DeactivateComponent(
        long id, PayrollSalaryVersionRequest request, CancellationToken cancellationToken) =>
        Execute<PayrollSalaryComponentResponse>(async () => Ok(await sender.Send(
            new SavePayrollSalaryComponentCommand(id, request.ExpectedVersion, null),
            cancellationToken)));

    /// <summary>Lists reusable salary structure templates for the validated Business.</summary>
    [HttpGet("salary-structures")]
    [Authorize(Policy = "payroll.salary.view")]
    public Task<ActionResult<IReadOnlyList<PayrollSalaryStructureResponse>>> Structures(
        [FromQuery] string? search, CancellationToken cancellationToken) =>
        Execute<IReadOnlyList<PayrollSalaryStructureResponse>>(async () => Ok(await sender.Send(
            new GetPayrollSalaryStructuresQuery(search), cancellationToken)));

    /// <summary>Gets one reusable salary structure without disclosing other Businesses.</summary>
    [HttpGet("salary-structures/{id:long}")]
    [Authorize(Policy = "payroll.salary.view")]
    public Task<ActionResult<PayrollSalaryStructureResponse>> Structure(
        long id, CancellationToken cancellationToken) =>
        Execute<PayrollSalaryStructureResponse>(async () => Ok(await sender.Send(
            new GetPayrollSalaryStructureQuery(id), cancellationToken)));

    /// <summary>Creates a reusable salary structure template, not an employee salary setup.</summary>
    [HttpPost("salary-structures")]
    [Authorize(Policy = "payroll.salary.manage")]
    public Task<ActionResult<PayrollSalaryStructureResponse>> CreateStructure(
        PayrollSalaryStructureData data, CancellationToken cancellationToken) =>
        Execute<PayrollSalaryStructureResponse>(async () =>
        {
            var row = await sender.Send(
                new SavePayrollSalaryStructureCommand(null, null, data), cancellationToken);
            return CreatedAtAction(nameof(Structure), new { id = row.Id }, row);
        });

    /// <summary>Updates a reusable salary structure using its expected concurrency version.</summary>
    [HttpPut("salary-structures/{id:long}")]
    [Authorize(Policy = "payroll.salary.manage")]
    public Task<ActionResult<PayrollSalaryStructureResponse>> UpdateStructure(
        long id, PayrollSalaryStructureUpdateRequest request, CancellationToken cancellationToken) =>
        Execute<PayrollSalaryStructureResponse>(async () => Ok(await sender.Send(
            new SavePayrollSalaryStructureCommand(id, request.ExpectedVersion, request.Data),
            cancellationToken)));

    /// <summary>Deactivates a reusable salary structure template.</summary>
    [HttpPost("salary-structures/{id:long}/deactivate")]
    [Authorize(Policy = "payroll.salary.manage")]
    public Task<ActionResult<PayrollSalaryStructureResponse>> DeactivateStructure(
        long id, PayrollSalaryVersionRequest request, CancellationToken cancellationToken) =>
        Execute<PayrollSalaryStructureResponse>(async () => Ok(await sender.Send(
            new SavePayrollSalaryStructureCommand(id, request.ExpectedVersion, null),
            cancellationToken)));

    /// <summary>Creates an audited, idempotent initial employee salary request.</summary>
    [HttpPost("salary-setup-requests")]
    [Authorize(Policy = "payroll.salary.manage")]
    [Authorize(Policy = "payroll.salary_setup.create")]
    public Task<ActionResult<PayrollSalarySetupRequestResponse>> CreateInitialSalarySetup(
        CreatePayrollSalarySetupRequest request, CancellationToken cancellationToken) =>
        Execute<PayrollSalarySetupRequestResponse>(async () =>
        {
            var result = await sender.Send(new CreatePayrollSalarySetupCommand(request), cancellationToken);
            return result.IsReplay ? Ok(result) : CreatedAtAction(
                nameof(SalarySetupRequest), new { id = result.Id }, result);
        });

    /// <summary>Reads a confidential initial salary setup request and approval history.</summary>
    [HttpGet("salary-setup-requests/{id:long}")]
    [Authorize(Policy = "payroll.salary.view")]
    public Task<ActionResult<PayrollSalarySetupRequestResponse>> SalarySetupRequest(
        long id, CancellationToken cancellationToken) =>
        Execute<PayrollSalarySetupRequestResponse>(async () => Ok(await sender.Send(
            new GetPayrollSalarySetupRequestQuery(id), cancellationToken)));

    /// <summary>Lists an employee's confidential initial salary setup requests.</summary>
    [HttpGet("employees/{employeeId:long}/salary-setup-requests")]
    [Authorize(Policy = "payroll.salary.view")]
    public Task<ActionResult<IReadOnlyList<PayrollSalarySetupRequestResponse>>> EmployeeSalarySetupRequests(
        long employeeId, CancellationToken cancellationToken) =>
        Execute<IReadOnlyList<PayrollSalarySetupRequestResponse>>(async () => Ok(await sender.Send(
            new GetEmployeeSalarySetupRequestsQuery(employeeId), cancellationToken)));

    /// <summary>Creates a confidential, effective-dated successor salary proposal.</summary>
    [HttpPost("salary-revision-requests")]
    [Authorize(Policy = "payroll.salary.manage")]
    [Authorize(Policy = "payroll.salary_revision.create")]
    public Task<ActionResult<PayrollSalaryRevisionRequestResponse>> CreateSalaryRevision(
        CreatePayrollSalaryRevisionRequest request, CancellationToken cancellationToken) =>
        Execute<PayrollSalaryRevisionRequestResponse>(async () =>
        {
            var result = await sender.Send(new CreatePayrollSalaryRevisionCommand(request), cancellationToken);
            return result.IsReplay ? Ok(result) : CreatedAtAction(
                nameof(SalaryRevisionRequest), new { id = result.Id }, result);
        });

    /// <summary>Reads a confidential salary revision proposal and its approval evidence.</summary>
    [HttpGet("salary-revision-requests/{id:long}")]
    [Authorize(Policy = "payroll.salary.view")]
    public Task<ActionResult<PayrollSalaryRevisionRequestResponse>> SalaryRevisionRequest(
        long id, CancellationToken cancellationToken) =>
        Execute<PayrollSalaryRevisionRequestResponse>(async () => Ok(await sender.Send(
            new GetPayrollSalaryRevisionRequestQuery(id), cancellationToken)));

    /// <summary>Lists an employee's confidential salary revision requests.</summary>
    [HttpGet("employees/{employeeId:long}/salary-revision-requests")]
    [Authorize(Policy = "payroll.salary.view")]
    public Task<ActionResult<IReadOnlyList<PayrollSalaryRevisionRequestResponse>>> EmployeeSalaryRevisionRequests(
        long employeeId, CancellationToken cancellationToken) =>
        Execute<IReadOnlyList<PayrollSalaryRevisionRequestResponse>>(async () => Ok(await sender.Send(
            new GetEmployeeSalaryRevisionRequestsQuery(employeeId), cancellationToken)));

    /// <summary>Submits a salary revision proposal through the Approval Kernel.</summary>
    [HttpPost("salary-revision-requests/{id:long}/submit")]
    [Authorize(Policy = "payroll.salary_revision.submit")]
    public Task<ActionResult<ApprovalDecisionResult>> SubmitSalaryRevision(
        long id, PayrollSalarySetupVersionRequest request, CancellationToken cancellationToken) =>
        Execute<ApprovalDecisionResult>(async () => Ok(await sender.Send(
            new SubmitPayrollSalaryRevisionCommand(id, request.ExpectedVersion), cancellationToken)));

    /// <summary>Approves a salary revision using the Approval Kernel.</summary>
    [HttpPost("salary-revision-approvals/{instanceId:long}/approve")]
    [Authorize(Policy = "payroll.salary_revision.approve")]
    public Task<ActionResult<ApprovalDecisionResult>> ApproveSalaryRevision(
        long instanceId, PayrollSalarySetupDecisionRequest request, CancellationToken cancellationToken) =>
        Execute<ApprovalDecisionResult>(async () => Ok(await sender.Send(
            new RecordApprovalDecisionCommand(
                instanceId, request.DecisionKey, ApprovalDecisionType.Approve, request.Reason),
            cancellationToken)));

    /// <summary>Rejects a salary revision using the Approval Kernel.</summary>
    [HttpPost("salary-revision-approvals/{instanceId:long}/reject")]
    [Authorize(Policy = "payroll.salary_revision.approve")]
    public Task<ActionResult<ApprovalDecisionResult>> RejectSalaryRevision(
        long instanceId, PayrollSalarySetupDecisionRequest request, CancellationToken cancellationToken) =>
        Execute<ApprovalDecisionResult>(async () => Ok(await sender.Send(
            new RecordApprovalDecisionCommand(
                instanceId, request.DecisionKey, ApprovalDecisionType.Reject, request.Reason),
            cancellationToken)));

    /// <summary>Cancels a pending salary revision using the Approval Kernel.</summary>
    [HttpPost("salary-revision-approvals/{instanceId:long}/cancel")]
    [Authorize(Policy = "payroll.salary_revision.submit")]
    public Task<ActionResult<ApprovalDecisionResult>> CancelSalaryRevision(
        long instanceId, PayrollSalarySetupDecisionRequest request, CancellationToken cancellationToken) =>
        Execute<ApprovalDecisionResult>(async () => Ok(await sender.Send(
            new CancelApprovalCommand(instanceId, request.DecisionKey, request.Reason ?? ""),
            cancellationToken)));

    /// <summary>Submits a draft initial salary setup through the Approval Kernel.</summary>
    [HttpPost("salary-setup-requests/{id:long}/submit")]
    [Authorize(Policy = "payroll.salary_setup.submit")]
    public Task<ActionResult<ApprovalDecisionResult>> SubmitInitialSalarySetup(
        long id, PayrollSalarySetupVersionRequest request, CancellationToken cancellationToken) =>
        Execute<ApprovalDecisionResult>(async () => Ok(await sender.Send(
            new SubmitPayrollSalarySetupCommand(id, request.ExpectedVersion), cancellationToken)));

    /// <summary>Records an Approval Kernel decision for a salary setup instance.</summary>
    [HttpPost("salary-setup-approvals/{instanceId:long}/approve")]
    [Authorize(Policy = "payroll.salary_setup.approve")]
    public Task<ActionResult<ApprovalDecisionResult>> ApproveInitialSalarySetup(
        long instanceId, PayrollSalarySetupDecisionRequest request, CancellationToken cancellationToken) =>
        Execute<ApprovalDecisionResult>(async () => Ok(await sender.Send(
            new RecordApprovalDecisionCommand(
                instanceId, request.DecisionKey, ApprovalDecisionType.Approve, request.Reason),
            cancellationToken)));

    /// <summary>Rejects a salary setup through the Approval Kernel.</summary>
    [HttpPost("salary-setup-approvals/{instanceId:long}/reject")]
    [Authorize(Policy = "payroll.salary_setup.approve")]
    public Task<ActionResult<ApprovalDecisionResult>> RejectInitialSalarySetup(
        long instanceId, PayrollSalarySetupDecisionRequest request, CancellationToken cancellationToken) =>
        Execute<ApprovalDecisionResult>(async () => Ok(await sender.Send(
            new RecordApprovalDecisionCommand(
                instanceId, request.DecisionKey, ApprovalDecisionType.Reject, request.Reason),
            cancellationToken)));

    /// <summary>Cancels a pending salary setup through the Approval Kernel.</summary>
    [HttpPost("salary-setup-approvals/{instanceId:long}/cancel")]
    [Authorize(Policy = "payroll.salary_setup.submit")]
    public Task<ActionResult<ApprovalDecisionResult>> CancelInitialSalarySetup(
        long instanceId, PayrollSalarySetupDecisionRequest request, CancellationToken cancellationToken) =>
        Execute<ApprovalDecisionResult>(async () => Ok(await sender.Send(
            new CancelApprovalCommand(instanceId, request.DecisionKey, request.Reason ?? ""),
            cancellationToken)));

    /// <summary>Reads the effective salary applicable on or before a date.</summary>
    [HttpGet("employees/{employeeId:long}/salary")]
    [Authorize(Policy = "payroll.salary.view")]
    public Task<ActionResult<PayrollEmployeeSalaryResponse>> EffectiveEmployeeSalary(
        long employeeId, [FromQuery] DateOnly? asOf, CancellationToken cancellationToken) =>
        Execute<PayrollEmployeeSalaryResponse>(async () => Ok(await sender.Send(
            new GetEffectiveEmployeeSalaryQuery(employeeId, asOf), cancellationToken)));

    /// <summary>Reads approved employee salary history without exposing mutable structure values.</summary>
    [HttpGet("employees/{employeeId:long}/salary-history")]
    [Authorize(Policy = "payroll.salary.view")]
    public Task<ActionResult<IReadOnlyList<PayrollEmployeeSalaryResponse>>> EmployeeSalaryHistory(
        long employeeId, CancellationToken cancellationToken) =>
        Execute<IReadOnlyList<PayrollEmployeeSalaryResponse>>(async () => Ok(await sender.Send(
            new GetEmployeeSalaryHistoryQuery(employeeId), cancellationToken)));

    /// <summary>Reads effective employee allowances, deductions and one-time adjustments.</summary>
    [HttpGet("employees/{employeeId:long}/adjustments")]
    [Authorize(Policy = "payroll.allowance_deduction.view")]
    public Task<ActionResult<IReadOnlyList<PayrollEmployeeAdjustmentResponse>>> EmployeeAdjustments(
        long employeeId, [FromQuery] DateOnly? asOf, CancellationToken cancellationToken) =>
        Execute<IReadOnlyList<PayrollEmployeeAdjustmentResponse>>(async () => Ok(await sender.Send(
            new GetPayrollEmployeeAdjustmentsQuery(
                employeeId, asOf ?? DateOnly.FromDateTime(DateTime.UtcNow)),
            cancellationToken)));

    /// <summary>Reads immutable version history for one employee adjustment.</summary>
    [HttpGet("adjustments/{adjustmentIdentity:guid}/history")]
    [Authorize(Policy = "payroll.allowance_deduction.view")]
    public Task<ActionResult<IReadOnlyList<PayrollEmployeeAdjustmentResponse>>> AdjustmentHistory(
        Guid adjustmentIdentity, CancellationToken cancellationToken) =>
        Execute<IReadOnlyList<PayrollEmployeeAdjustmentResponse>>(async () => Ok(await sender.Send(
            new GetPayrollEmployeeAdjustmentHistoryQuery(adjustmentIdentity), cancellationToken)));

    /// <summary>Creates a recurring or one-time employee payroll adjustment.</summary>
    [HttpPost("adjustments")]
    [Authorize(Policy = "payroll.allowance_deduction.manage")]
    public Task<ActionResult<PayrollEmployeeAdjustmentResponse>> CreateAdjustment(
        SavePayrollEmployeeAdjustmentRequest request, CancellationToken cancellationToken) =>
        Execute<PayrollEmployeeAdjustmentResponse>(async () =>
        {
            var result = await sender.Send(
                new SavePayrollEmployeeAdjustmentCommand(request), cancellationToken);
            return result.IsReplay ? Ok(result) : CreatedAtAction(
                nameof(AdjustmentHistory), new { adjustmentIdentity = result.AdjustmentIdentity }, result);
        });

    /// <summary>Creates an immutable successor version of an effective recurring adjustment.</summary>
    [HttpPut("adjustments/{adjustmentIdentity:guid}")]
    [Authorize(Policy = "payroll.allowance_deduction.manage")]
    public Task<ActionResult<PayrollEmployeeAdjustmentResponse>> UpdateAdjustment(
        Guid adjustmentIdentity, SavePayrollEmployeeAdjustmentRequest request,
        CancellationToken cancellationToken) =>
        Execute<PayrollEmployeeAdjustmentResponse>(async () =>
        {
            if (request.AdjustmentIdentity != adjustmentIdentity)
                throw new ArgumentException("The route and payload adjustment identities must match.");
            var result = await sender.Send(
                new SavePayrollEmployeeAdjustmentCommand(request), cancellationToken);
            return Ok(result);
        });

    /// <summary>Appends an inactive successor to end a recurring adjustment.</summary>
    [HttpPost("adjustments/{adjustmentIdentity:guid}/end")]
    [Authorize(Policy = "payroll.allowance_deduction.manage")]
    public Task<ActionResult<PayrollEmployeeAdjustmentResponse>> EndAdjustment(
        Guid adjustmentIdentity, EndPayrollEmployeeAdjustmentRequest request,
        CancellationToken cancellationToken) =>
        Execute<PayrollEmployeeAdjustmentResponse>(async () =>
        {
            if (request.AdjustmentIdentity != adjustmentIdentity)
                throw new ArgumentException("The route and payload adjustment identities must match.");
            return Ok(await sender.Send(
                new EndPayrollEmployeeAdjustmentCommand(request), cancellationToken));
        });

    private async Task<ActionResult<T>> Execute<T>(Func<Task<ActionResult<T>>> action)
    {
        try
        {
            return await action();
        }
        catch (UnauthorizedAccessException exception)
        {
            logger.LogWarning("Payroll salary setup access rejected: {Message}", exception.Message);
            return Problem(statusCode: StatusCodes.Status403Forbidden, title: "Payroll access denied.");
        }
        catch (ArgumentException exception)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid salary setup.", detail: exception.Message);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (PayrollSalaryConflictException exception)
        {
            logger.LogWarning("Payroll salary setup conflict: {Message}", exception.Message);
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "Salary setup conflict.", detail: exception.Message);
        }
        catch (ApprovalKernelConflictException exception)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "Approval conflict.", detail: exception.Message);
        }
        catch (ApprovalKernelUnavailableException exception)
        {
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Approval is unavailable.", detail: exception.Message);
        }
    }
}

/// <summary>Payload for a versioned salary component update.</summary>
public sealed record PayrollSalaryComponentUpdateRequest(
    long ExpectedVersion, PayrollSalaryComponentData Data);

/// <summary>Payload for a versioned reusable salary structure update.</summary>
public sealed record PayrollSalaryStructureUpdateRequest(
    long ExpectedVersion, PayrollSalaryStructureData Data);

/// <summary>Expected concurrency version used when deactivating a salary configuration.</summary>
public sealed record PayrollSalaryVersionRequest(long ExpectedVersion);

/// <summary>Expected salary setup request version for submission.</summary>
public sealed record PayrollSalarySetupVersionRequest(long ExpectedVersion);

/// <summary>Idempotency key and optional reason for a salary approval decision.</summary>
public sealed record PayrollSalarySetupDecisionRequest(string DecisionKey, string? Reason);
