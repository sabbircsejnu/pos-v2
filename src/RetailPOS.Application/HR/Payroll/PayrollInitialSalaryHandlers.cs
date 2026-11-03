using MediatR;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.HR.Approvals;
using RetailPOS.Application.HR.Capabilities;
using RetailPOS.Core.Entities.HR.Approvals;

namespace RetailPOS.Application.HR.Payroll;

public sealed record CreatePayrollSalarySetupCommand(CreatePayrollSalarySetupRequest Data)
    : IRequest<PayrollSalarySetupRequestResponse>;
public sealed record GetPayrollSalarySetupRequestQuery(long Id)
    : IRequest<PayrollSalarySetupRequestResponse>;
public sealed record GetEmployeeSalarySetupRequestsQuery(long EmployeeId)
    : IRequest<IReadOnlyList<PayrollSalarySetupRequestResponse>>;
public sealed record GetEmployeeSalaryHistoryQuery(long EmployeeId)
    : IRequest<IReadOnlyList<PayrollEmployeeSalaryResponse>>;
public sealed record GetEffectiveEmployeeSalaryQuery(long EmployeeId, DateOnly? AsOf = null)
    : IRequest<PayrollEmployeeSalaryResponse>;
public sealed record SubmitPayrollSalarySetupCommand(long Id, long ExpectedVersion)
    : IRequest<ApprovalDecisionResult>;

public sealed class PayrollInitialSalaryHandlers(
    IHrPayrollCapabilityScope scope,
    IHrPayrollCapabilityService capabilities,
    IPayrollSalarySetupAccess access,
    IPayrollSalarySetupStore store,
    IRequiredAuditTransaction audit,
    ISender sender,
    IApprovalTargetSnapshotReader approvalTargets,
    TimeProvider clock) :
    IRequestHandler<CreatePayrollSalarySetupCommand, PayrollSalarySetupRequestResponse>,
    IRequestHandler<GetPayrollSalarySetupRequestQuery, PayrollSalarySetupRequestResponse>,
    IRequestHandler<GetEmployeeSalarySetupRequestsQuery, IReadOnlyList<PayrollSalarySetupRequestResponse>>,
    IRequestHandler<GetEmployeeSalaryHistoryQuery, IReadOnlyList<PayrollEmployeeSalaryResponse>>,
    IRequestHandler<GetEffectiveEmployeeSalaryQuery, PayrollEmployeeSalaryResponse>,
    IRequestHandler<SubmitPayrollSalarySetupCommand, ApprovalDecisionResult>
{
    public async Task<PayrollSalarySetupRequestResponse> Handle(
        CreatePayrollSalarySetupCommand command, CancellationToken cancellationToken)
    {
        Validate(command.Data);
        await EnsureCapabilities(cancellationToken);
        var businessId = scope.RequireBusinessId();
        var actorUserId = access.RequireRealUserId();
        var locationId = await store.EmployeePrimaryLocationAsync(
            businessId, command.Data.EmployeeId, command.Data.EffectiveFrom, cancellationToken);
        await access.EnsureAsync(businessId, locationId, "payroll.salary.manage", cancellationToken);
        await access.EnsureAsync(businessId, locationId, "payroll.salary_setup.create", cancellationToken);

        return await audit.ExecuteAsync(
            new RequiredAuditOperation(businessId, "payroll.salary_setup.created", "Payroll",
                "Created an initial salary setup request.", "PayrollSalarySetupRequest", "new"),
            token => store.CreateInitialRequestAsync(businessId, actorUserId, command.Data, token),
            result => !result.IsReplay,
            cancellationToken);
    }

    public async Task<IReadOnlyList<PayrollSalarySetupRequestResponse>> Handle(
        GetEmployeeSalarySetupRequestsQuery query, CancellationToken cancellationToken)
    {
        await EnsureCapabilities(cancellationToken);
        var businessId = scope.RequireBusinessId();
        var currentLocation = await store.EmployeePrimaryLocationAsync(
            businessId, query.EmployeeId, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime),
            cancellationToken);
        await access.EnsureAsync(businessId, currentLocation, "payroll.salary.view", cancellationToken);
        var requests = await store.EmployeeRequestsAsync(businessId, query.EmployeeId, cancellationToken);
        foreach (var request in requests)
            await access.EnsureAsync(businessId, request.OperationalLocationProjectionId,
                "payroll.salary.view", cancellationToken);
        return await audit.ExecuteAsync(
            new RequiredAuditOperation(businessId, "payroll.salary_setup.history_accessed", "Payroll",
                "Accessed confidential employee salary setup history.", "HrEmployee",
                query.EmployeeId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            _ => Task.FromResult(requests),
            cancellationToken);
    }

    public async Task<PayrollSalarySetupRequestResponse> Handle(
        GetPayrollSalarySetupRequestQuery query, CancellationToken cancellationToken)
    {
        await EnsureCapabilities(cancellationToken);
        var businessId = scope.RequireBusinessId();
        return await audit.ExecuteAsync(
            new RequiredAuditOperation(businessId, "payroll.salary_setup.accessed", "Payroll",
                "Accessed an initial salary setup request.", "PayrollSalarySetupRequest",
                query.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            async token =>
            {
                var request = await store.RequestAsync(businessId, query.Id, token);
                await access.EnsureAsync(businessId, request.OperationalLocationProjectionId,
                    "payroll.salary.view", token);
                return request;
            },
            cancellationToken);
    }

    public async Task<IReadOnlyList<PayrollEmployeeSalaryResponse>> Handle(
        GetEmployeeSalaryHistoryQuery query, CancellationToken cancellationToken)
    {
        await EnsureCapabilities(cancellationToken);
        var businessId = scope.RequireBusinessId();
        var currentLocation = await store.EmployeePrimaryLocationAsync(
            businessId, query.EmployeeId, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime),
            cancellationToken);
        await access.EnsureAsync(businessId, currentLocation, "payroll.salary.view", cancellationToken);
        var history = await store.EmployeeSalaryHistoryAsync(businessId, query.EmployeeId, null, cancellationToken);
        foreach (var salary in history)
            await access.EnsureAsync(businessId, salary.OperationalLocationProjectionId,
                "payroll.salary.view", cancellationToken);
        return await audit.ExecuteAsync(
            new RequiredAuditOperation(businessId, "payroll.employee_salary.history_accessed", "Payroll",
                "Accessed confidential employee salary history.", "HrEmployee",
                query.EmployeeId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            _ => Task.FromResult(history),
            cancellationToken);
    }

    public async Task<PayrollEmployeeSalaryResponse> Handle(
        GetEffectiveEmployeeSalaryQuery query, CancellationToken cancellationToken)
    {
        await EnsureCapabilities(cancellationToken);
        var businessId = scope.RequireBusinessId();
        var currentLocation = await store.EmployeePrimaryLocationAsync(
            businessId, query.EmployeeId, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime),
            cancellationToken);
        await access.EnsureAsync(businessId, currentLocation, "payroll.salary.view", cancellationToken);
        var asOf = query.AsOf ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var history = await store.EmployeeSalaryHistoryAsync(
            businessId, query.EmployeeId, asOf, cancellationToken);
        var salary = history.OrderByDescending(row => row.EffectiveFrom).ThenByDescending(row => row.Id)
            .FirstOrDefault() ?? throw new KeyNotFoundException("No effective salary exists for this employee and date.");
        await access.EnsureAsync(businessId, salary.OperationalLocationProjectionId,
            "payroll.salary.view", cancellationToken);
        return await audit.ExecuteAsync(
            new RequiredAuditOperation(businessId, "payroll.employee_salary.accessed", "Payroll",
                "Accessed confidential effective employee salary.", "HrEmployee",
                query.EmployeeId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            _ => Task.FromResult(salary),
            cancellationToken);
    }

    public async Task<ApprovalDecisionResult> Handle(
        SubmitPayrollSalarySetupCommand command, CancellationToken cancellationToken)
    {
        await EnsureCapabilities(cancellationToken);
        var businessId = scope.RequireBusinessId();
        var request = await store.RequestAsync(businessId, command.Id, cancellationToken);
        await access.EnsureAsync(businessId, request.OperationalLocationProjectionId,
            "payroll.salary_setup.submit", cancellationToken);
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
            throw new PayrollSalaryConflictException("Only a draft salary setup request can be submitted.");
        }
        if (request.Version != command.ExpectedVersion)
            throw new PayrollSalaryConflictException("Salary setup request changed since it was read.");

        var snapshot = await approvalTargets.ReadAsync(
            businessId, PayrollSalarySetupApprovalConstants.TargetType,
            request.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), cancellationToken)
            ?? throw new PayrollSalaryConflictException("Salary setup approval evidence is unavailable.");
        return await sender.Send(new SubmitApprovalCommand(
            ApprovalProcessType.SalarySetup,
            PayrollSalarySetupApprovalConstants.TargetType,
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

    private static void Validate(CreatePayrollSalarySetupRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.EmployeeId <= 0 || request.SalaryStructureId <= 0)
            throw new ArgumentException("Employee and salary structure are required.");
        if (request.Components is null || request.Components.Count is < 1 or > 100
            || request.Components.Any(line => line.SalaryComponentId <= 0)
            || request.Components.Select(line => line.SalaryComponentId).Distinct().Count() != request.Components.Count)
            throw new ArgumentException("Salary setup requires 1-100 distinct component values.");
        if (request.Components.Any(line => line.Value is < 0
                || line.Value.HasValue && decimal.Round(line.Value.Value, 4) != line.Value.Value))
            throw new ArgumentException("Component values must be non-negative and have at most four decimal places.");
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 1000)
            throw new ArgumentException("A reason of at most 1000 characters is required.");
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey)
            || request.IdempotencyKey.Trim().Length > 150)
            throw new ArgumentException("An idempotency key of at most 150 characters is required.");
    }
}
