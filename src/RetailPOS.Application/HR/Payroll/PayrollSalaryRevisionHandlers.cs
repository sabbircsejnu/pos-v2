using MediatR;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.HR.Approvals;
using RetailPOS.Application.HR.Capabilities;
using RetailPOS.Core.Entities.HR.Approvals;

namespace RetailPOS.Application.HR.Payroll;

public sealed record CreatePayrollSalaryRevisionCommand(CreatePayrollSalaryRevisionRequest Data)
    : IRequest<PayrollSalaryRevisionRequestResponse>;
public sealed record GetPayrollSalaryRevisionRequestQuery(long Id)
    : IRequest<PayrollSalaryRevisionRequestResponse>;
public sealed record GetEmployeeSalaryRevisionRequestsQuery(long EmployeeId)
    : IRequest<IReadOnlyList<PayrollSalaryRevisionRequestResponse>>;
public sealed record SubmitPayrollSalaryRevisionCommand(long Id, long ExpectedVersion)
    : IRequest<ApprovalDecisionResult>;

public sealed class PayrollSalaryRevisionHandlers(
    IHrPayrollCapabilityScope scope,
    IHrPayrollCapabilityService capabilities,
    IPayrollSalarySetupAccess access,
    IPayrollSalaryRevisionStore revisions,
    IPayrollSalarySetupStore salaries,
    IRequiredAuditTransaction audit,
    ISender sender,
    IApprovalTargetSnapshotReader approvalTargets,
    TimeProvider clock) :
    IRequestHandler<CreatePayrollSalaryRevisionCommand, PayrollSalaryRevisionRequestResponse>,
    IRequestHandler<GetPayrollSalaryRevisionRequestQuery, PayrollSalaryRevisionRequestResponse>,
    IRequestHandler<GetEmployeeSalaryRevisionRequestsQuery, IReadOnlyList<PayrollSalaryRevisionRequestResponse>>,
    IRequestHandler<SubmitPayrollSalaryRevisionCommand, ApprovalDecisionResult>
{
    public async Task<PayrollSalaryRevisionRequestResponse> Handle(
        CreatePayrollSalaryRevisionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command.Data);
        await EnsureCapabilities(cancellationToken);
        var businessId = scope.RequireBusinessId();
        var locationId = await salaries.EmployeePrimaryLocationAsync(
            businessId, command.Data.EmployeeId, command.Data.EffectiveFrom, cancellationToken);
        await access.EnsureAsync(businessId, locationId, "payroll.salary.manage", cancellationToken);
        await access.EnsureAsync(businessId, locationId, "payroll.salary_revision.create", cancellationToken);
        return await audit.ExecuteAsync(
            new RequiredAuditOperation(businessId, "payroll.salary_revision.created", "Payroll",
                "Created a salary revision request.", "PayrollSalaryRevisionRequest", "new"),
            token => revisions.CreateAsync(
                businessId, access.RequireRealUserId(), command.Data, token),
            result => !result.IsReplay,
            cancellationToken);
    }

    public async Task<PayrollSalaryRevisionRequestResponse> Handle(
        GetPayrollSalaryRevisionRequestQuery query, CancellationToken cancellationToken)
    {
        await EnsureCapabilities(cancellationToken);
        var businessId = scope.RequireBusinessId();
        return await audit.ExecuteAsync(
            new RequiredAuditOperation(businessId, "payroll.salary_revision.accessed", "Payroll",
                "Accessed a confidential salary revision request.", "PayrollSalaryRevisionRequest",
                query.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            async token =>
            {
                var row = await revisions.RequestAsync(businessId, query.Id, token);
                await access.EnsureAsync(
                    businessId, row.OperationalLocationProjectionId, "payroll.salary.view", token);
                return row;
            },
            cancellationToken);
    }

    public async Task<IReadOnlyList<PayrollSalaryRevisionRequestResponse>> Handle(
        GetEmployeeSalaryRevisionRequestsQuery query, CancellationToken cancellationToken)
    {
        await EnsureCapabilities(cancellationToken);
        var businessId = scope.RequireBusinessId();
        var location = await salaries.EmployeePrimaryLocationAsync(
            businessId, query.EmployeeId, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime),
            cancellationToken);
        await access.EnsureAsync(businessId, location, "payroll.salary.view", cancellationToken);
        var rows = await revisions.EmployeeRequestsAsync(businessId, query.EmployeeId, cancellationToken);
        foreach (var row in rows)
            await access.EnsureAsync(
                businessId, row.OperationalLocationProjectionId, "payroll.salary.view", cancellationToken);
        return await audit.ExecuteAsync(
            new RequiredAuditOperation(businessId, "payroll.salary_revision.history_accessed", "Payroll",
                "Accessed confidential employee salary revision history.", "HrEmployee",
                query.EmployeeId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            _ => Task.FromResult(rows),
            cancellationToken);
    }

    public async Task<ApprovalDecisionResult> Handle(
        SubmitPayrollSalaryRevisionCommand command, CancellationToken cancellationToken)
    {
        await EnsureCapabilities(cancellationToken);
        var businessId = scope.RequireBusinessId();
        var request = await revisions.RequestAsync(businessId, command.Id, cancellationToken);
        await access.EnsureAsync(
            businessId, request.OperationalLocationProjectionId, "payroll.salary_revision.submit",
            cancellationToken);
        if (request.Status != "Draft")
        {
            if (request.ApprovalInstanceId.HasValue
                && (request.SubmittedByUserId == access.RequireRealUserId()
                    || request.Status is "Approved" or "Rejected" or "Cancelled"))
            {
                var status = request.Status switch
                {
                    "Approved" => ApprovalInstanceStatus.Approved,
                    "Rejected" => ApprovalInstanceStatus.Rejected,
                    "Cancelled" => ApprovalInstanceStatus.Cancelled,
                    _ => ApprovalInstanceStatus.Pending
                };
                return new ApprovalDecisionResult(request.ApprovalInstanceId.Value, status, null, true);
            }
            throw new PayrollSalaryConflictException("Only a draft salary revision can be submitted.");
        }
        if (request.Version != command.ExpectedVersion)
            throw new PayrollSalaryConflictException("Salary revision changed since it was read.");
        var snapshot = await approvalTargets.ReadAsync(
            businessId, PayrollSalaryRevisionApprovalConstants.TargetType,
            request.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), cancellationToken)
            ?? throw new PayrollSalaryConflictException("Salary revision approval evidence is unavailable.");
        return await sender.Send(new SubmitApprovalCommand(
            ApprovalProcessType.SalaryRevision,
            PayrollSalaryRevisionApprovalConstants.TargetType,
            request.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ApprovalScopeType.Location,
            request.OperationalLocationProjectionId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            snapshot.Version,
            snapshot.Hash), cancellationToken);
    }

    private async Task EnsureCapabilities(CancellationToken cancellationToken)
    {
        await capabilities.EnsureEnabledAsync(HrPayrollCapabilityKeys.Hr, cancellationToken);
        await capabilities.EnsureEnabledAsync(HrPayrollCapabilityKeys.Payroll, cancellationToken);
    }
}
