using MediatR;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.HR.Capabilities;

namespace RetailPOS.Application.HR.Payroll;

public sealed record GetPayrollEmployeeAdjustmentsQuery(long EmployeeId, DateOnly AsOf)
    : IRequest<IReadOnlyList<PayrollEmployeeAdjustmentResponse>>;
public sealed record GetPayrollEmployeeAdjustmentHistoryQuery(Guid AdjustmentIdentity)
    : IRequest<IReadOnlyList<PayrollEmployeeAdjustmentResponse>>;
public sealed record SavePayrollEmployeeAdjustmentCommand(SavePayrollEmployeeAdjustmentRequest Data)
    : IRequest<PayrollEmployeeAdjustmentResponse>;
public sealed record EndPayrollEmployeeAdjustmentCommand(EndPayrollEmployeeAdjustmentRequest Data)
    : IRequest<PayrollEmployeeAdjustmentResponse>;

public sealed class PayrollEmployeeAdjustmentHandlers(
    IHrPayrollCapabilityScope scope,
    IHrPayrollCapabilityService capabilities,
    IPayrollSalarySetupAccess access,
    IPayrollSalarySetupStore salaries,
    IPayrollEmployeeAdjustmentStore adjustments,
    IRequiredAuditTransaction audit,
    TimeProvider? clock = null) :
    IRequestHandler<GetPayrollEmployeeAdjustmentsQuery, IReadOnlyList<PayrollEmployeeAdjustmentResponse>>,
    IRequestHandler<GetPayrollEmployeeAdjustmentHistoryQuery, IReadOnlyList<PayrollEmployeeAdjustmentResponse>>,
    IRequestHandler<SavePayrollEmployeeAdjustmentCommand, PayrollEmployeeAdjustmentResponse>,
    IRequestHandler<EndPayrollEmployeeAdjustmentCommand, PayrollEmployeeAdjustmentResponse>
{
    public async Task<IReadOnlyList<PayrollEmployeeAdjustmentResponse>> Handle(
        GetPayrollEmployeeAdjustmentsQuery query, CancellationToken cancellationToken)
    {
        await EnsureCapabilities(cancellationToken);
        var businessId = scope.RequireBusinessId();
        var location = await salaries.EmployeePrimaryLocationAsync(
            businessId, query.EmployeeId, query.AsOf, cancellationToken);
        await access.EnsureAsync(
            businessId, location, "payroll.allowance_deduction.view", cancellationToken);
        return await audit.ExecuteAsync(
            new RequiredAuditOperation(businessId, "payroll.allowance_deduction.accessed", "Payroll",
                "Accessed confidential employee payroll adjustments.", "HrEmployee",
                query.EmployeeId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            async token =>
            {
                var rows = await adjustments.EffectiveAsync(
                    businessId, query.EmployeeId, query.AsOf, token);
                foreach (var row in rows)
                    await access.EnsureAsync(
                        businessId, row.OperationalLocationProjectionId,
                        "payroll.allowance_deduction.view", token);
                return rows;
            },
            cancellationToken);
    }

    public async Task<IReadOnlyList<PayrollEmployeeAdjustmentResponse>> Handle(
        GetPayrollEmployeeAdjustmentHistoryQuery query, CancellationToken cancellationToken)
    {
        await EnsureCapabilities(cancellationToken);
        var businessId = scope.RequireBusinessId();
        return await audit.ExecuteAsync(
            new RequiredAuditOperation(businessId, "payroll.allowance_deduction.history_accessed", "Payroll",
                "Accessed confidential adjustment version history.", "PayrollEmployeeAdjustmentVersion",
                query.AdjustmentIdentity.ToString("D")),
            async token =>
            {
                var rows = await adjustments.HistoryAsync(
                    businessId, query.AdjustmentIdentity, token);
                if (rows.Count > 0)
                {
                    var currentLocation = await salaries.EmployeePrimaryLocationAsync(
                        businessId, rows[0].EmployeeId,
                        DateOnly.FromDateTime((clock ?? TimeProvider.System).GetUtcNow().UtcDateTime), token);
                    await access.EnsureAsync(
                        businessId, currentLocation, "payroll.allowance_deduction.view", token);
                }
                foreach (var row in rows)
                    await access.EnsureAsync(
                        businessId, row.OperationalLocationProjectionId,
                        "payroll.allowance_deduction.view", token);
                return rows;
            },
            cancellationToken);
    }

    public async Task<PayrollEmployeeAdjustmentResponse> Handle(
        SavePayrollEmployeeAdjustmentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command.Data);
        await EnsureCapabilities(cancellationToken);
        var businessId = scope.RequireBusinessId();
        var locationId = await salaries.EmployeePrimaryLocationAsync(
            businessId, command.Data.EmployeeId, command.Data.EffectiveFrom, cancellationToken);
        await access.EnsureAsync(
            businessId, locationId, "payroll.allowance_deduction.manage", cancellationToken);
        return await audit.ExecuteAsync(
            new RequiredAuditOperation(businessId, "payroll.allowance_deduction.saved", "Payroll",
                "Created or versioned an employee payroll adjustment.", "PayrollEmployeeAdjustmentVersion",
                command.Data.AdjustmentIdentity?.ToString("D") ?? "new"),
            token => adjustments.SaveAsync(
                businessId, access.RequireRealUserId(), command.Data, token),
            result => !result.IsReplay,
            cancellationToken);
    }

    public async Task<PayrollEmployeeAdjustmentResponse> Handle(
        EndPayrollEmployeeAdjustmentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command.Data);
        await EnsureCapabilities(cancellationToken);
        var businessId = scope.RequireBusinessId();
        var history = await adjustments.HistoryAsync(
            businessId, command.Data.AdjustmentIdentity, cancellationToken);
        var current = history.LastOrDefault()
            ?? throw new KeyNotFoundException("Payroll adjustment was not found in this Business.");
        await access.EnsureAsync(
            businessId, current.OperationalLocationProjectionId,
            "payroll.allowance_deduction.manage", cancellationToken);
        return await audit.ExecuteAsync(
            new RequiredAuditOperation(businessId, "payroll.allowance_deduction.ended", "Payroll",
                "Ended a recurring employee payroll adjustment.", "PayrollEmployeeAdjustmentVersion",
                command.Data.AdjustmentIdentity.ToString("D")),
            token => adjustments.EndAsync(
                businessId, access.RequireRealUserId(), command.Data, token),
            result => !result.IsReplay,
            cancellationToken);
    }

    private async Task EnsureCapabilities(CancellationToken cancellationToken)
    {
        await capabilities.EnsureEnabledAsync(HrPayrollCapabilityKeys.Hr, cancellationToken);
        await capabilities.EnsureEnabledAsync(HrPayrollCapabilityKeys.Payroll, cancellationToken);
    }
}
