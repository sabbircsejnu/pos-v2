using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RetailPOS.Core.Entities.HR.Overtime;

namespace RetailPOS.Application.HR.Overtime;

public sealed record HrOvertimeCalculationInput(
    int WorkedMinutes,
    int BreakMinutes,
    int ScheduledNetMinutes,
    int? ActualMinutesBeforeShift,
    int? ActualMinutesAfterShift,
    string DayCategory);

public sealed record HrOvertimeCalculationResult(
    int EligibleMinutes,
    int CalculatedMinutes,
    string DayCategory,
    string InputsJson,
    string Fingerprint);

public static class HrOvertimeRules
{
    public static string CreateRequestHash(
        long attendanceId,
        long employeeId,
        DateOnly workDate,
        int attendanceVersion,
        long policyId,
        int policyVersion,
        int minutes,
        string category,
        string fingerprint)
    {
        var payload = JsonSerializer.Serialize(new
        {
            attendanceId,
            employeeId,
            workDate,
            attendanceVersion,
            policyId,
            policyVersion,
            minutes,
            category,
            fingerprint
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    public static void ValidatePolicy(HrOvertimePolicyVersion policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (policy.BusinessId <= 0 || string.IsNullOrWhiteSpace(policy.Code)
            || string.IsNullOrWhiteSpace(policy.Name) || policy.VersionNo <= 0
            || policy.EffectiveFrom == default
            || policy.EffectiveTo.HasValue && policy.EffectiveTo.Value < policy.EffectiveFrom)
            throw new ArgumentException("Business, policy identity, version and valid effective dates are required.");
        var needsReference = policy.ScopeType != HrOvertimePolicyScopeType.Business;
        if (needsReference != !string.IsNullOrWhiteSpace(policy.ScopeReference))
            throw new ArgumentException("Policy scope reference does not match the scope type.");
        if (!Enum.IsDefined(policy.ScopeType) || !Enum.IsDefined(policy.RoundingMethod)
            || !Enum.IsDefined(policy.RateType))
            throw new ArgumentException("Overtime policy contains an unsupported scope, rounding or rate type.");
        if (policy.ScopeType == HrOvertimePolicyScopeType.Location
            && (!long.TryParse(policy.ScopeReference, out var locationId) || locationId <= 0)
            || policy.ScopeType == HrOvertimePolicyScopeType.Employee
            && (!long.TryParse(policy.ScopeReference, out var employeeId) || employeeId <= 0))
            throw new ArgumentException("Location and employee policy references must be positive numeric IDs.");
        if (policy.ScopeType == HrOvertimePolicyScopeType.LocationType
            && policy.ScopeReference is not ("Outlet" or "Warehouse" or "Factory"))
            throw new ArgumentException("Location-type overtime policies must use a supported operational location type.");
        if (policy.MinimumEligibleMinutes < 0 || policy.MinimumEligibleMinutes > 1440
            || policy.MaximumEligibleMinutes is < 0 or > 2880
            || policy.MaximumEligibleMinutes.HasValue
                && policy.MaximumEligibleMinutes.Value < policy.MinimumEligibleMinutes
            || policy.RoundingUnitMinutes is < 1 or > 1440)
            throw new ArgumentException("Overtime thresholds, caps and rounding unit are outside supported limits.");
        if (!policy.AllowNormalWorkingDay && !policy.AllowWeeklyOff
            && !policy.AllowHoliday && !policy.AllowSpecialDay)
            throw new ArgumentException("At least one day category must be eligible.");
        if (policy.NormalDayRate < 0 || policy.WeeklyOffRate < 0
            || policy.HolidayRate < 0 || policy.SpecialDayRate < 0)
            throw new ArgumentException("Overtime rates cannot be negative.");
        if (policy.RateType == HrOvertimeRateType.Multiplier
            && (policy.NormalDayRate > 100 || policy.WeeklyOffRate > 100
                || policy.HolidayRate > 100 || policy.SpecialDayRate > 100))
            throw new ArgumentException("Overtime multipliers cannot exceed 100.");
    }

    public static HrOvertimeCalculationResult Calculate(
        HrOvertimeCalculationInput input,
        HrOvertimePolicyVersion policy)
    {
        ArgumentNullException.ThrowIfNull(input);
        ValidatePolicy(policy);
        if (input.WorkedMinutes < 0 || input.BreakMinutes < 0
            || input.BreakMinutes > input.WorkedMinutes || input.ScheduledNetMinutes < 0
            || input.DayCategory is not ("NormalWorkingDay" or "WeeklyOff" or "Holiday" or "SpecialDay"))
            throw new ArgumentException("Overtime calculation inputs are invalid.");

        var enabledForDay = input.DayCategory switch
        {
            "NormalWorkingDay" => policy.AllowNormalWorkingDay,
            "WeeklyOff" => policy.AllowWeeklyOff,
            "Holiday" => policy.AllowHoliday,
            "SpecialDay" => policy.AllowSpecialDay,
            _ => false
        };
        var eligible = 0;
        if (enabledForDay)
        {
            eligible = input.DayCategory == "NormalWorkingDay"
                ? Math.Max(0, input.WorkedMinutes - input.ScheduledNetMinutes)
                : input.WorkedMinutes;
            if (!policy.IncludePreShift && input.ActualMinutesBeforeShift is > 0)
                eligible = Math.Max(0, eligible - input.ActualMinutesBeforeShift.Value);
            if (!policy.IncludePostShift && input.ActualMinutesAfterShift is > 0)
                eligible = Math.Max(0, eligible - input.ActualMinutesAfterShift.Value);
            if (eligible < policy.MinimumEligibleMinutes)
                eligible = 0;
            if (policy.MaximumEligibleMinutes.HasValue)
                eligible = Math.Min(eligible, policy.MaximumEligibleMinutes.Value);
        }

        var rounded = Round(eligible, policy.RoundingUnitMinutes, policy.RoundingMethod);
        var inputsJson = JsonSerializer.Serialize(input);
        var evidence = JsonSerializer.Serialize(new
        {
            Input = input,
            PolicyId = policy.Id,
            PolicyVersion = policy.VersionNo,
            policy.MinimumEligibleMinutes,
            policy.MaximumEligibleMinutes,
            policy.RoundingUnitMinutes,
            policy.RoundingMethod,
            EligibleMinutes = eligible,
            CalculatedMinutes = rounded
        });
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(evidence)));
        return new HrOvertimeCalculationResult(eligible, rounded, input.DayCategory, inputsJson, fingerprint);
    }

    private static int Round(int minutes, int unit, HrOvertimeRoundingMethod method)
    {
        if (minutes == 0)
            return 0;
        var units = (decimal)minutes / unit;
        var roundedUnits = method switch
        {
            HrOvertimeRoundingMethod.Down => decimal.Floor(units),
            HrOvertimeRoundingMethod.Up => decimal.Ceiling(units),
            HrOvertimeRoundingMethod.Nearest => decimal.Round(units, 0, MidpointRounding.AwayFromZero),
            _ => throw new ArgumentOutOfRangeException(nameof(method))
        };
        return checked((int)(roundedUnits * unit));
    }
}
