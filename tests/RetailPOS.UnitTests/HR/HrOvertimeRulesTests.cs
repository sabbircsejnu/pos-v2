using RetailPOS.Application.HR.Overtime;
using RetailPOS.Core.Entities.HR.Overtime;

namespace RetailPOS.UnitTests.HR;

public sealed class HrOvertimeRulesTests
{
    [Fact]
    public void CrossMidnightNormalShiftUsesNetWorkedMinutesWithoutDeductingBreakAgain()
    {
        var result = HrOvertimeRules.Calculate(
            new HrOvertimeCalculationInput(930, 30, 930, 0, 0, "NormalWorkingDay"),
            Policy());

        Assert.Equal(0, result.EligibleMinutes);
        Assert.Equal(0, result.CalculatedMinutes);
    }

    [Fact]
    public void CrossMidnightShiftOvertimeIsMeasuredFromShiftStartWorkDateNetSchedule()
    {
        var policy = Policy();
        policy.IncludePostShift = true;
        var result = HrOvertimeRules.Calculate(
            new HrOvertimeCalculationInput(945, 30, 930, 0, 15, "NormalWorkingDay"),
            policy);

        Assert.Equal(15, result.EligibleMinutes);
        Assert.Equal(15, result.CalculatedMinutes);
    }

    [Fact]
    public void DayOffWorkedMinutesAreAlreadyBreakNetAndAreNotDeductedAgain()
    {
        var policy = Policy();
        policy.AllowNormalWorkingDay = false;
        policy.AllowWeeklyOff = true;
        var result = HrOvertimeRules.Calculate(
            new HrOvertimeCalculationInput(450, 30, 0, null, null, "WeeklyOff"),
            policy);

        Assert.Equal(450, result.EligibleMinutes);
        Assert.Equal(450, result.CalculatedMinutes);
    }

    [Theory]
    [InlineData(HrOvertimeRoundingMethod.Down, 40)]
    [InlineData(HrOvertimeRoundingMethod.Nearest, 50)]
    [InlineData(HrOvertimeRoundingMethod.Up, 50)]
    public void ThresholdCapAndRoundingAreAppliedDeterministically(
        HrOvertimeRoundingMethod method, int expected)
    {
        var policy = Policy();
        policy.MinimumEligibleMinutes = 40;
        policy.MaximumEligibleMinutes = 47;
        policy.RoundingUnitMinutes = 10;
        policy.RoundingMethod = method;
        policy.IncludePostShift = true;
        var result = HrOvertimeRules.Calculate(
            new HrOvertimeCalculationInput(530, 0, 480, 0, 50, "NormalWorkingDay"),
            policy);

        Assert.Equal(47, result.EligibleMinutes);
        Assert.Equal(expected, result.CalculatedMinutes);
    }

    [Fact]
    public void CalculationFingerprintIsStableForEquivalentInputsAndPolicy()
    {
        var input = new HrOvertimeCalculationInput(510, 30, 480, 0, 0, "NormalWorkingDay");

        var first = HrOvertimeRules.Calculate(input, Policy());
        var second = HrOvertimeRules.Calculate(input, Policy());

        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal(
            HrOvertimeRules.CreateRequestHash(1, 2, new DateOnly(2026, 1, 1), 1, 9, 1,
                first.CalculatedMinutes, first.DayCategory, first.Fingerprint),
            HrOvertimeRules.CreateRequestHash(1, 2, new DateOnly(2026, 1, 1), 1, 9, 1,
                second.CalculatedMinutes, second.DayCategory, second.Fingerprint));
    }

    [Fact]
    public void UnsupportedPolicyScopeReferenceIsRejected()
    {
        var policy = Policy();
        policy.ScopeType = HrOvertimePolicyScopeType.LocationType;
        policy.ScopeReference = "Unknown";
        Assert.Throws<ArgumentException>(() => HrOvertimeRules.ValidatePolicy(policy));
    }

    private static HrOvertimePolicyVersion Policy() => new()
    {
        Id = 9,
        BusinessId = 1,
        Code = "STANDARD",
        Name = "Standard",
        ScopeType = HrOvertimePolicyScopeType.Business,
        VersionNo = 1,
        EffectiveFrom = new DateOnly(2026, 1, 1),
        MinimumEligibleMinutes = 1,
        RoundingUnitMinutes = 1,
        RoundingMethod = HrOvertimeRoundingMethod.Down,
        AllowNormalWorkingDay = true,
        AllowWeeklyOff = false,
        AllowHoliday = false,
        AllowSpecialDay = false,
        RateType = HrOvertimeRateType.Multiplier,
        NormalDayRate = 1,
        WeeklyOffRate = 1,
        HolidayRate = 1,
        SpecialDayRate = 1,
        IsActive = true
    };
}
