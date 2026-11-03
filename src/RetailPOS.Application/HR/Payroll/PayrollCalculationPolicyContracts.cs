using MediatR;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.HR.Capabilities;

namespace RetailPOS.Application.HR.Payroll;

public sealed record CreatePayrollCalculationPolicyRequest(
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    string? PartialMonthBasis,
    IReadOnlyList<long> SalaryBaseComponentIds,
    bool DeductAbsence,
    bool DeductUnpaidLeave,
    decimal? LatePerMinuteRate,
    decimal? EarlyLeavePerMinuteRate,
    string? OvertimeMode,
    int OvertimeDivisorDays,
    int StandardPaidMinutes,
    decimal? NormalDayOvertimeRate,
    decimal? WeeklyOffOvertimeRate,
    decimal? HolidayOvertimeRate,
    decimal? SpecialDayOvertimeRate,
    long? AbsenceComponentId,
    long? UnpaidLeaveComponentId,
    long? LateComponentId,
    long? EarlyLeaveComponentId,
    long? OvertimeComponentId,
    long? TaxStatutoryComponentId);

public sealed record PayrollCalculationPolicyResponse(
    long Id,
    long BusinessId,
    int VersionNo,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    string PartialMonthBasis,
    IReadOnlyList<long> SalaryBaseComponentIds,
    bool DeductAbsence,
    bool DeductUnpaidLeave,
    decimal? LatePerMinuteRate,
    decimal? EarlyLeavePerMinuteRate,
    string? OvertimeMode,
    int OvertimeDivisorDays,
    int StandardPaidMinutes,
    decimal? NormalDayOvertimeRate,
    decimal? WeeklyOffOvertimeRate,
    decimal? HolidayOvertimeRate,
    decimal? SpecialDayOvertimeRate,
    long? AbsenceComponentId,
    long? UnpaidLeaveComponentId,
    long? LateComponentId,
    long? EarlyLeaveComponentId,
    long? OvertimeComponentId,
    long? TaxStatutoryComponentId,
    string ConfigurationSnapshotJson,
    DateTime CreatedAtUtc,
    long CreatedByUserId);

public interface IPayrollCalculationPolicyStore
{
    Task<PayrollCalculationPolicyResponse> CreateAsync(
        long businessId, long actorUserId, CreatePayrollCalculationPolicyRequest request,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<PayrollCalculationPolicyResponse>> ListAsync(
        long businessId, CancellationToken cancellationToken);
}

public sealed record CreatePayrollCalculationPolicyCommand(CreatePayrollCalculationPolicyRequest Data)
    : IRequest<PayrollCalculationPolicyResponse>;
public sealed record GetPayrollCalculationPoliciesQuery
    : IRequest<IReadOnlyList<PayrollCalculationPolicyResponse>>;

public sealed class PayrollCalculationPolicyHandlers(
    IHrPayrollCapabilityService capabilities,
    IPayrollRunAccess access,
    IPayrollCalculationPolicyStore store,
    IRequiredAuditTransaction audit) :
    IRequestHandler<CreatePayrollCalculationPolicyCommand, PayrollCalculationPolicyResponse>,
    IRequestHandler<GetPayrollCalculationPoliciesQuery, IReadOnlyList<PayrollCalculationPolicyResponse>>
{
    public async Task<PayrollCalculationPolicyResponse> Handle(
        CreatePayrollCalculationPolicyCommand command, CancellationToken cancellationToken)
    {
        var request = command.Data with
        {
            PartialMonthBasis = string.IsNullOrWhiteSpace(command.Data.PartialMonthBasis)
                ? "Fixed30"
                : command.Data.PartialMonthBasis.Trim()
        };
        Validate(request);
        await EnsureCapabilitiesAsync(cancellationToken);
        var businessId = access.RequireBusinessId();
        var actorUserId = access.RequireRealUserId();
        await access.EnsurePermissionAsync(
            businessId, "payroll.calculation_policy.manage", cancellationToken);
        return await audit.ExecuteAsync(
            new RequiredAuditOperation(businessId, "payroll.calculation_policy.created", "Payroll",
                "Created an immutable Payroll calculation policy version.",
                "PayrollCalculationPolicyVersion",
                request.EffectiveFrom.ToString("yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture)),
                token => store.CreateAsync(businessId, actorUserId, request, token),
            cancellationToken);
    }

    public async Task<IReadOnlyList<PayrollCalculationPolicyResponse>> Handle(
        GetPayrollCalculationPoliciesQuery query, CancellationToken cancellationToken)
    {
        await EnsureCapabilitiesAsync(cancellationToken);
        var businessId = access.RequireBusinessId();
        await access.EnsurePermissionAsync(
            businessId, "payroll.calculation_policy.view", cancellationToken);
        return await store.ListAsync(businessId, cancellationToken);
    }

    private async Task EnsureCapabilitiesAsync(CancellationToken cancellationToken)
    {
        await capabilities.EnsureEnabledAsync(HrPayrollCapabilityKeys.Hr, cancellationToken);
        await capabilities.EnsureEnabledAsync(HrPayrollCapabilityKeys.Payroll, cancellationToken);
    }

    private static void Validate(CreatePayrollCalculationPolicyRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.EffectiveFrom == default || request.EffectiveFrom == DateOnly.MaxValue)
            throw new ArgumentException("Policy effective-from must be a valid date.");
        if (request.EffectiveTo < request.EffectiveFrom)
            throw new ArgumentException("Policy effective-to must not be before effective-from.");
        if (request.PartialMonthBasis is not ("CalendarDays" or "Fixed30" or "WorkingDays"))
            throw new ArgumentException("Partial-month basis must be CalendarDays, Fixed30, or WorkingDays.");
        if (request.SalaryBaseComponentIds is null
            || request.SalaryBaseComponentIds.Any(id => id <= 0)
            || request.SalaryBaseComponentIds.Distinct().Count() != request.SalaryBaseComponentIds.Count)
            throw new ArgumentException("Salary-base component IDs must be positive and unique.");
        if (request.OvertimeDivisorDays <= 0 || request.StandardPaidMinutes < 0)
            throw new ArgumentException("Overtime divisor must be positive and standard paid minutes cannot be negative.");
        if (request.OvertimeMode is not (null or "FixedHourlyRate" or "SalaryBaseMultiplier"))
            throw new ArgumentException("Overtime mode is not supported.");
        var rates = new[]
        {
            request.LatePerMinuteRate, request.EarlyLeavePerMinuteRate,
            request.NormalDayOvertimeRate, request.WeeklyOffOvertimeRate,
            request.HolidayOvertimeRate, request.SpecialDayOvertimeRate
        };
        if (rates.Any(rate => rate < 0m))
            throw new ArgumentException("Payroll calculation rates cannot be negative.");
        if (request.DeductAbsence && request.AbsenceComponentId is null)
            throw new ArgumentException("Absence deduction requires an output component.");
        if (request.DeductUnpaidLeave && request.UnpaidLeaveComponentId is null)
            throw new ArgumentException("Unpaid-leave deduction requires an output component.");
        if (request.LatePerMinuteRate.HasValue != request.LateComponentId.HasValue)
            throw new ArgumentException("Late-minute pricing and its output component must be configured together.");
        if (request.EarlyLeavePerMinuteRate.HasValue != request.EarlyLeaveComponentId.HasValue)
            throw new ArgumentException("Early-leave pricing and its output component must be configured together.");
        if (request.OvertimeMode is not null
            && (request.OvertimeComponentId is null || request.StandardPaidMinutes <= 0
                || request.NormalDayOvertimeRate is null || request.WeeklyOffOvertimeRate is null
                || request.HolidayOvertimeRate is null || request.SpecialDayOvertimeRate is null))
            throw new ArgumentException("Overtime pricing requires a component, paid-minutes basis, and rates for all day categories.");
        if (request.OvertimeMode is null
            && (request.OvertimeComponentId.HasValue || request.NormalDayOvertimeRate.HasValue
                || request.WeeklyOffOvertimeRate.HasValue || request.HolidayOvertimeRate.HasValue
                || request.SpecialDayOvertimeRate.HasValue))
            throw new ArgumentException("Overtime rate details require a configured overtime mode.");
        if ((request.DeductAbsence || request.DeductUnpaidLeave
                || request.OvertimeMode == "SalaryBaseMultiplier")
            && request.SalaryBaseComponentIds.Count == 0)
            throw new ArgumentException("Salary-based deduction and overtime pricing requires salary-base components.");
        var componentIds = new[]
        {
            request.AbsenceComponentId, request.UnpaidLeaveComponentId, request.LateComponentId,
            request.EarlyLeaveComponentId, request.OvertimeComponentId, request.TaxStatutoryComponentId
        }.Where(id => id.HasValue).Select(id => id!.Value);
        if (componentIds.Any(id => id <= 0))
            throw new ArgumentException("Output component IDs must be positive.");
    }
}
