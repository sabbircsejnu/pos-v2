using MediatR;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.HR.Approvals;
using RetailPOS.Application.HR.Capabilities;
using RetailPOS.Core.Entities.HR.Approvals;

namespace RetailPOS.Application.HR.Payroll;

public sealed record CreatePayrollEmployeeLoanCommand(CreatePayrollEmployeeLoanRequest Data)
    : IRequest<PayrollEmployeeLoanResponse>;
public sealed record GetPayrollEmployeeLoanQuery(long Id) : IRequest<PayrollEmployeeLoanResponse>;
public sealed record GetEmployeePayrollLoansQuery(long EmployeeId)
    : IRequest<IReadOnlyList<PayrollEmployeeLoanResponse>>;
public sealed record SubmitPayrollEmployeeLoanCommand(long Id, long ExpectedVersion)
    : IRequest<ApprovalDecisionResult>;
public sealed record UpdatePayrollLoanScheduleCommand(long Id, UpdatePayrollLoanScheduleRequest Data)
    : IRequest<PayrollEmployeeLoanResponse>;
public sealed record RecordPayrollLoanRepaymentCommand(long Id, RecordPayrollLoanRepaymentRequest Data)
    : IRequest<PayrollEmployeeLoanResponse>;

public sealed class PayrollEmployeeLoanHandlers(
    IHrPayrollCapabilityScope scope,
    IHrPayrollCapabilityService capabilities,
    IPayrollSalarySetupAccess access,
    IPayrollSalarySetupStore salaries,
    IPayrollEmployeeLoanStore loans,
    IRequiredAuditTransaction audit,
    ISender sender,
    IApprovalTargetSnapshotReader approvalTargets,
    TimeProvider? clock = null) :
    IRequestHandler<CreatePayrollEmployeeLoanCommand, PayrollEmployeeLoanResponse>,
    IRequestHandler<GetPayrollEmployeeLoanQuery, PayrollEmployeeLoanResponse>,
    IRequestHandler<GetEmployeePayrollLoansQuery, IReadOnlyList<PayrollEmployeeLoanResponse>>,
    IRequestHandler<SubmitPayrollEmployeeLoanCommand, ApprovalDecisionResult>,
    IRequestHandler<UpdatePayrollLoanScheduleCommand, PayrollEmployeeLoanResponse>,
    IRequestHandler<RecordPayrollLoanRepaymentCommand, PayrollEmployeeLoanResponse>
{
    public async Task<PayrollEmployeeLoanResponse> Handle(
        CreatePayrollEmployeeLoanCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command.Data);
        await EnsureCapabilities(cancellationToken);
        var businessId = scope.RequireBusinessId();
        var actorUserId = access.RequireRealUserId();
        var locationId = await salaries.EmployeePrimaryLocationAsync(
            businessId, command.Data.EmployeeId, command.Data.RecoveryStartDate, cancellationToken);
        await access.EnsureAsync(businessId, locationId, "payroll.loan.manage", cancellationToken);
        return await audit.ExecuteAsync(
            new RequiredAuditOperation(businessId, "payroll.loan.created", "Payroll",
                "Created an employee loan or advance request.", "PayrollEmployeeLoan", "new"),
            token => loans.CreateAsync(businessId, actorUserId, command.Data, token),
            result => !result.IsReplay,
            cancellationToken);
    }

    public async Task<PayrollEmployeeLoanResponse> Handle(
        GetPayrollEmployeeLoanQuery query, CancellationToken cancellationToken)
    {
        await EnsureCapabilities(cancellationToken);
        var businessId = scope.RequireBusinessId();
        return await audit.ExecuteAsync(
            new RequiredAuditOperation(businessId, "payroll.loan.accessed", "Payroll",
                "Accessed confidential employee loan and repayment history.",
                "PayrollEmployeeLoan", query.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            async token =>
            {
                var loan = await loans.RequestAsync(businessId, query.Id, token);
                await EnsureLoanAccess(businessId, loan, "payroll.loan.view", token);
                return loan;
            },
            cancellationToken);
    }

    public async Task<IReadOnlyList<PayrollEmployeeLoanResponse>> Handle(
        GetEmployeePayrollLoansQuery query, CancellationToken cancellationToken)
    {
        await EnsureCapabilities(cancellationToken);
        var businessId = scope.RequireBusinessId();
        var today = Today();
        var locationId = await salaries.EmployeePrimaryLocationAsync(
            businessId, query.EmployeeId, today, cancellationToken);
        await access.EnsureAsync(businessId, locationId, "payroll.loan.view", cancellationToken);
        return await audit.ExecuteAsync(
            new RequiredAuditOperation(businessId, "payroll.loan.history_accessed", "Payroll",
                "Accessed confidential employee loan history.", "HrEmployee",
                query.EmployeeId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            async token =>
            {
                var rows = await loans.EmployeeLoansAsync(businessId, query.EmployeeId, token);
                foreach (var loan in rows)
                    await EnsureLoanAccess(businessId, loan, "payroll.loan.view", token);
                return rows;
            },
            cancellationToken);
    }

    public async Task<ApprovalDecisionResult> Handle(
        SubmitPayrollEmployeeLoanCommand command, CancellationToken cancellationToken)
    {
        if (command.Id <= 0 || command.ExpectedVersion <= 0)
            throw new ArgumentException("A valid loan ID and expected version are required.");
        await EnsureCapabilities(cancellationToken);
        var businessId = scope.RequireBusinessId();
        var loan = await loans.RequestAsync(businessId, command.Id, cancellationToken);
        await EnsureLoanAccess(businessId, loan, "payroll.loan.manage", cancellationToken);
        if (loan.Version != command.ExpectedVersion)
            throw new PayrollSalaryConflictException("Employee loan changed since it was read.");
        var snapshot = await approvalTargets.ReadAsync(
            businessId, PayrollEmployeeLoanApprovalConstants.TargetType,
            loan.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), cancellationToken)
            ?? throw new PayrollSalaryConflictException("Employee loan approval evidence is unavailable.");
        return await sender.Send(new SubmitApprovalCommand(
            ApprovalProcessType.EmployeeLoan,
            PayrollEmployeeLoanApprovalConstants.TargetType,
            loan.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ApprovalScopeType.Location,
            loan.OperationalLocationProjectionId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            snapshot.Version,
            snapshot.Hash), cancellationToken);
    }

    public async Task<PayrollEmployeeLoanResponse> Handle(
        UpdatePayrollLoanScheduleCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command.Data);
        await EnsureCapabilities(cancellationToken);
        var businessId = scope.RequireBusinessId();
        var actorUserId = access.RequireRealUserId();
        var loan = await loans.RequestAsync(businessId, command.Id, cancellationToken);
        await EnsureLoanAccess(businessId, loan, "payroll.loan.manage", cancellationToken);
        return await audit.ExecuteAsync(
            new RequiredAuditOperation(businessId, "payroll.loan.schedule_updated", "Payroll",
                "Updated an employee loan recovery schedule.", "PayrollEmployeeLoan",
                command.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            token => loans.UpdateScheduleAsync(
                businessId, actorUserId, command.Id, command.Data, token),
            result => !result.IsReplay,
            cancellationToken);
    }

    public async Task<PayrollEmployeeLoanResponse> Handle(
        RecordPayrollLoanRepaymentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command.Data);
        await EnsureCapabilities(cancellationToken);
        var businessId = scope.RequireBusinessId();
        var actorUserId = access.RequireRealUserId();
        var loan = await loans.RequestAsync(businessId, command.Id, cancellationToken);
        await EnsureLoanAccess(businessId, loan, "payroll.loan.manage", cancellationToken);
        return await audit.ExecuteAsync(
            new RequiredAuditOperation(businessId, "payroll.loan.repayment_recorded", "Payroll",
                "Recorded an employee loan repayment.", "PayrollEmployeeLoan",
                command.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            token => loans.RecordRepaymentAsync(
                businessId, actorUserId, command.Id, command.Data, token),
            result => !result.IsReplay,
            cancellationToken);
    }

    private async Task EnsureLoanAccess(
        long businessId, PayrollEmployeeLoanResponse loan, string permission,
        CancellationToken cancellationToken)
    {
        await access.EnsureAsync(
            businessId, loan.OperationalLocationProjectionId, permission, cancellationToken);
        var currentLocation = await salaries.EmployeePrimaryLocationAsync(
            businessId, loan.EmployeeId, Today(), cancellationToken);
        await access.EnsureAsync(businessId, currentLocation, permission, cancellationToken);
    }

    private async Task EnsureCapabilities(CancellationToken cancellationToken)
    {
        await capabilities.EnsureEnabledAsync(HrPayrollCapabilityKeys.Hr, cancellationToken);
        await capabilities.EnsureEnabledAsync(HrPayrollCapabilityKeys.Payroll, cancellationToken);
    }

    private DateOnly Today() =>
        DateOnly.FromDateTime((clock ?? TimeProvider.System).GetUtcNow().UtcDateTime);
}
