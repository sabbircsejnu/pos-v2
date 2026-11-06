using RetailPOS.Application.Outlets;
using RetailPOS.Application.Outlets.SalesTargets;
using RetailPOS.Application.OwnerDashboards;

namespace RetailPOS.Tests.Outlets;

public sealed class SalesTargetCalculatorTests
{
    /// <summary>The worked example: Basundhara 1, closed on Tuesday, weekly target 87,000.</summary>
    private static readonly IReadOnlyList<SalesTargetDaySnapshot> Basundhara =
    [
        new(DayOfWeek.Sunday, false, 12_000m),
        new(DayOfWeek.Monday, false, 12_000m),
        new(DayOfWeek.Tuesday, true, null),
        new(DayOfWeek.Wednesday, false, 12_000m),
        new(DayOfWeek.Thursday, false, 16_000m),
        new(DayOfWeek.Friday, false, 20_000m),
        new(DayOfWeek.Saturday, false, 15_000m)
    ];

    private static IReadOnlyList<SalesTargetDaySnapshot> Flat(decimal amount, params DayOfWeek[] off) =>
        Enum.GetValues<DayOfWeek>()
            .Select(day => off.Contains(day)
                ? new SalesTargetDaySnapshot(day, true, null)
                : new SalesTargetDaySnapshot(day, false, amount))
            .ToList();

    private static OutletSalesTargetSchedule Schedule(params SalesTargetVersionSnapshot[] versions) => new(1, versions, []);

    private static SalesTargetVersionSnapshot Version(int number, string from, IReadOnlyList<SalesTargetDaySnapshot> days, bool enabled = true) =>
        new(number, DateOnly.Parse(from), enabled, days);

    private static DateOnly D(string value) => DateOnly.Parse(value);

    // ---- Weekly off days ----------------------------------------------------------------------------------------

    [Fact]
    public void NoOffDays_EveryDayHasItsTarget()
    {
        var result = SalesTargetCalculator.Period(Schedule(Version(1, "2026-01-01", Flat(100m))), D("2026-11-07"), D("2026-11-13"));

        Assert.Equal(700m, result.Target);
        Assert.Equal((7, 0, 0), (result.WorkingDays, result.OffDays, result.NotConfiguredDays));
    }

    [Fact]
    public void SingleOffDay_ExactWorkedExample()
    {
        var schedule = Schedule(Version(1, "2026-01-01", Basundhara));

        var (from, to) = SalesTargetCalculator.WeekOf(D("2026-11-10"));
        var week = SalesTargetCalculator.Period(schedule, from, to);

        Assert.Equal((D("2026-11-07"), D("2026-11-13")), (from, to));
        Assert.Equal(87_000m, week.Target);
        Assert.Equal(1, week.OffDays);
    }

    [Fact]
    public void OffDay_IsZeroNotMissing()
    {
        var day = SalesTargetCalculator.Day(Schedule(Version(1, "2026-01-01", Basundhara)), D("2026-11-10"));

        Assert.Equal(SalesTargetDayKind.OffDay, day.Kind);
        Assert.Equal(0m, day.Amount);
    }

    [Fact]
    public void MultipleOffDays_AreAllExcluded()
    {
        var schedule = Schedule(Version(1, "2026-01-01", Flat(100m, DayOfWeek.Tuesday, DayOfWeek.Friday)));

        var week = SalesTargetCalculator.Period(schedule, D("2026-11-07"), D("2026-11-13"));

        Assert.Equal(500m, week.Target);
        Assert.Equal(2, week.OffDays);
    }

    [Fact]
    public void WorkingDayBecomesOff_ChangesOnlyFromItsEffectiveDate()
    {
        var schedule = Schedule(
            Version(1, "2026-01-01", Flat(100m)),
            Version(2, "2026-11-08", Flat(100m, DayOfWeek.Tuesday)));

        Assert.Equal(100m, SalesTargetCalculator.Day(schedule, D("2026-11-03")).Amount);
        Assert.Equal(0m, SalesTargetCalculator.Day(schedule, D("2026-11-10")).Amount);
    }

    [Fact]
    public void OffDayBecomesWorking_ChangesOnlyFromItsEffectiveDate()
    {
        var schedule = Schedule(
            Version(1, "2026-01-01", Flat(100m, DayOfWeek.Tuesday)),
            Version(2, "2026-11-08", Flat(250m)));

        Assert.Equal(SalesTargetDayKind.OffDay, SalesTargetCalculator.Day(schedule, D("2026-11-03")).Kind);
        Assert.Equal(250m, SalesTargetCalculator.Day(schedule, D("2026-11-10")).Amount);
    }

    [Fact]
    public void NoVersion_IsNotConfigured()
    {
        var result = SalesTargetCalculator.Period(OutletSalesTargetSchedule.Empty(1), D("2026-11-01"), D("2026-11-30"));

        Assert.Null(result.Target);
        Assert.Equal(30, result.NotConfiguredDays);
    }

    [Fact]
    public void DisabledVersion_IsNotConfiguredEvenOnAnOffDay()
    {
        var schedule = Schedule(Version(1, "2026-01-01", Flat(100m, DayOfWeek.Tuesday), enabled: false));

        Assert.Equal(SalesTargetDayKind.NotConfigured, SalesTargetCalculator.Day(schedule, D("2026-11-10")).Kind);
        Assert.Null(SalesTargetCalculator.Period(schedule, D("2026-11-01"), D("2026-11-30")).Target);
    }

    [Fact]
    public void TwoSavesForOneDate_TheLaterWins()
    {
        var schedule = Schedule(
            Version(2, "2026-11-08", Flat(200m)),
            Version(1, "2026-11-08", Flat(100m)));

        Assert.Equal(200m, SalesTargetCalculator.Day(schedule, D("2026-11-08")).Amount);
    }

    // ---- Periods ------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("2026-11-07", "2026-11-07")] // Saturday starts the week
    [InlineData("2026-11-13", "2026-11-07")] // Friday ends it
    [InlineData("2026-11-08", "2026-11-07")]
    public void Week_RunsSaturdayToFriday(string date, string expectedFrom)
    {
        var (from, to) = SalesTargetCalculator.WeekOf(D(date));

        Assert.Equal(D(expectedFrom), from);
        Assert.Equal(DayOfWeek.Saturday, from.DayOfWeek);
        Assert.Equal(DayOfWeek.Friday, to.DayOfWeek);
    }

    [Fact]
    public void HalfMonths_AreOneToFifteenAndSixteenToEnd_NotFourteenDays()
    {
        Assert.Equal((D("2026-11-01"), D("2026-11-15")), SalesTargetCalculator.FirstHalfOf(D("2026-11-20")));
        Assert.Equal((D("2026-11-16"), D("2026-11-30")), SalesTargetCalculator.SecondHalfOf(D("2026-11-02")));
        Assert.Equal((D("2026-01-16"), D("2026-01-31")), SalesTargetCalculator.SecondHalfOf(D("2026-01-02")));
    }

    [Fact]
    public void Month_CountsActualWeekdays_NeverWeeklyTimesFour()
    {
        // November 2026 has 30 days and four Tuesdays.
        var schedule = Schedule(Version(1, "2026-01-01", Flat(100m, DayOfWeek.Tuesday)));
        var november = SalesTargetCalculator.Period(schedule, D("2026-11-01"), D("2026-11-30"));
        Assert.Equal(2_600m, november.Target);
        Assert.Equal(4, november.OffDays);

        // December 2026 has 31 days and five Tuesdays, so the same schedule also gives 26 working days.
        var december = SalesTargetCalculator.Period(schedule, D("2026-12-01"), D("2026-12-31"));
        Assert.Equal(2_600m, december.Target);
        Assert.Equal(5, december.OffDays);
    }

    [Fact]
    public void LeapYearFebruary_HasTwentyNineDays()
    {
        var schedule = Schedule(Version(1, "2027-01-01", Flat(10m)));

        var (from, to) = SalesTargetCalculator.MonthOf(D("2028-02-10"));
        Assert.Equal(D("2028-02-29"), to);
        Assert.Equal(290m, SalesTargetCalculator.Period(schedule, from, to).Target);
        Assert.Equal(280m, SalesTargetCalculator.Period(schedule, D("2027-02-01"), D("2027-02-28")).Target);
    }

    [Fact]
    public void RangeAcrossMonths_WalksEachDate()
    {
        var schedule = Schedule(Version(1, "2026-01-01", Flat(100m)));

        Assert.Equal(300m, SalesTargetCalculator.Period(schedule, D("2026-11-29"), D("2026-12-01")).Target);
    }

    [Fact]
    public void EffectiveDateInsideAPeriod_UsesEachVersionForItsDays_AndFlagsTheGap()
    {
        var schedule = Schedule(Version(1, "2026-11-11", Flat(100m)));

        var week = SalesTargetCalculator.Period(schedule, D("2026-11-07"), D("2026-11-13"));

        Assert.Equal(300m, week.Target);
        Assert.Equal(4, week.NotConfiguredDays);
    }

    [Fact]
    public void DecimalAmounts_KeepTheirPaisa()
    {
        var schedule = Schedule(Version(1, "2026-01-01", Flat(100.10m)));

        Assert.Equal(700.70m, SalesTargetCalculator.Period(schedule, D("2026-11-07"), D("2026-11-13")).Target);
    }

    // ---- Overrides ----------------------------------------------------------------------------------------------

    private static OutletSalesTargetSchedule WithMonthlyOverride() => new(
        1,
        [Version(1, "2026-01-01", Flat(100m))],
        [new SalesTargetOverrideSnapshot(SalesTargetPeriodTypes.Monthly, D("2026-11-01"), D("2026-11-30"), 5_000m)]);

    [Fact]
    public void Override_ReplacesTheExactPeriodOnly()
    {
        var month = SalesTargetCalculator.Period(WithMonthlyOverride(), D("2026-11-01"), D("2026-11-30"));

        Assert.Equal(5_000m, month.Target);
        Assert.Equal(3_000m, month.Calculated);
        Assert.True(month.IsOverridden);
    }

    [Fact]
    public void Override_IsNotSpreadOverOtherRanges()
    {
        Assert.Equal(100m, SalesTargetCalculator.Day(WithMonthlyOverride(), D("2026-11-10")).Amount);

        var part = SalesTargetCalculator.Period(WithMonthlyOverride(), D("2026-11-01"), D("2026-11-15"));
        Assert.Equal(1_500m, part.Target);
        Assert.False(part.IsOverridden);
    }

    [Fact]
    public void Override_DoesNotReviveADisabledPeriod()
    {
        var schedule = new OutletSalesTargetSchedule(
            1,
            [Version(1, "2026-01-01", Flat(100m), enabled: false)],
            [new SalesTargetOverrideSnapshot(SalesTargetPeriodTypes.Monthly, D("2026-11-01"), D("2026-11-30"), 5_000m)]);

        var month = SalesTargetCalculator.Period(schedule, D("2026-11-01"), D("2026-11-30"));

        Assert.Null(month.Target);
        Assert.False(month.IsOverridden);
    }

    // ---- Input rules --------------------------------------------------------------------------------------------

    private static OutletSalesTargetInput Input(
        bool enabled = true,
        Func<DayOfWeek, OutletSalesTargetDayInput>? day = null,
        IReadOnlyList<OutletSalesTargetOverrideInput>? overrides = null) =>
        new(
            enabled,
            D("2026-11-06"),
            Enum.GetValues<DayOfWeek>().Select(day ?? (item => new OutletSalesTargetDayInput(item, false, 100m))).ToList(),
            overrides,
            null);

    private static string CodeOf(OutletSalesTargetInput input) =>
        Assert.Throws<OutletSetupException>(() => SalesTargetRules.Validate(input)).Code;

    [Fact]
    public void Rules_AcceptAValidConfiguration() => SalesTargetRules.Validate(Input());

    [Fact]
    public void Rules_RejectAllSevenDaysOff_EvenWhenDisabled()
    {
        var input = Input(enabled: false, day: item => new OutletSalesTargetDayInput(item, true, null));

        Assert.Equal(SalesTargetErrorCodes.AllDaysOff, CodeOf(input));
    }

    [Fact]
    public void Rules_RejectNegativeAmounts()
    {
        var input = Input(day: item => new OutletSalesTargetDayInput(item, false, item == DayOfWeek.Monday ? -1m : 1m));

        Assert.Equal(SalesTargetErrorCodes.InvalidConfiguration, CodeOf(input));
    }

    [Fact]
    public void Rules_RejectThreeDecimals()
    {
        var input = Input(day: item => new OutletSalesTargetDayInput(item, false, 1.005m));

        Assert.Equal(SalesTargetErrorCodes.InvalidConfiguration, CodeOf(input));
    }

    [Fact]
    public void Rules_RequireAnAmountForAnOpenDay_OnlyWhenEnabled()
    {
        OutletSalesTargetDayInput Blank(DayOfWeek item) => new(item, false, item == DayOfWeek.Tuesday ? null : 1m);

        Assert.Equal(SalesTargetErrorCodes.InvalidConfiguration, CodeOf(Input(day: Blank)));
        SalesTargetRules.Validate(Input(enabled: false, day: Blank));
    }

    [Fact]
    public void Rules_RequireSevenDistinctDays()
    {
        var input = new OutletSalesTargetInput(true, D("2026-11-06"), [new OutletSalesTargetDayInput(DayOfWeek.Monday, false, 1m)], null, null);

        Assert.Equal(SalesTargetErrorCodes.InvalidConfiguration, CodeOf(input));
    }

    [Theory]
    [InlineData(SalesTargetPeriodTypes.Weekly, "2026-11-08")] // A Sunday
    [InlineData(SalesTargetPeriodTypes.FirstHalf, "2026-11-16")]
    [InlineData(SalesTargetPeriodTypes.SecondHalf, "2026-11-01")]
    [InlineData(SalesTargetPeriodTypes.Monthly, "2026-11-02")]
    [InlineData("biweekly", "2026-11-01")] // D05: there is no 14-day period
    public void Rules_RejectAnOverrideThatIsNotACalendarPeriod(string type, string from)
    {
        var input = Input(overrides: [new OutletSalesTargetOverrideInput(type, D(from), 1m, null)]);

        Assert.Equal(SalesTargetErrorCodes.InvalidOverride, CodeOf(input));
    }

    [Fact]
    public void Rules_RejectANegativeOverride()
    {
        var input = Input(overrides: [new OutletSalesTargetOverrideInput(SalesTargetPeriodTypes.Monthly, D("2026-11-01"), -1m, null)]);

        Assert.Equal(SalesTargetErrorCodes.InvalidOverride, CodeOf(input));
    }

    [Fact]
    public void Normalize_DropsAnAmountOnAnOffDay()
    {
        var days = SalesTargetRules.Normalize(Enum.GetValues<DayOfWeek>().Select(item => new OutletSalesTargetDayInput(item, item == DayOfWeek.Tuesday, 5m)).ToList());

        Assert.Null(days.Single(day => day.DayOfWeek == DayOfWeek.Tuesday).TargetAmount);
    }

    // ---- Comparison with sales ----------------------------------------------------------------------------------

    private static SalesDayFact Sale(long outlet, string date, decimal net) => new(outlet, D(date), net, 1, 0m, 0);

    private static OutletSalesTargetSchedule ScheduleFor(long outletId, params SalesTargetVersionSnapshot[] versions) =>
        new(outletId, versions, []);

    [Fact]
    public void Shortfall_WhenSalesAreBelowTarget()
    {
        var figures = SalesTargetPerformance.ForShowroom(
            ScheduleFor(1, Version(1, "2026-01-01", Flat(100m))), D("2026-11-11"), D("2026-11-11"), [Sale(1, "2026-11-11", 60m)]);

        var total = SalesTargetPerformance.Aggregate([figures]);

        Assert.Equal((100m, 40m, 0m, 60m), (total.Target, total.Shortfall, total.Exceeded, total.AchievementPercent));
        Assert.Equal(MetricStatuses.Ok, total.Status);
    }

    [Fact]
    public void Exceeded_WhenSalesAreAboveTarget()
    {
        var figures = SalesTargetPerformance.ForShowroom(
            ScheduleFor(1, Version(1, "2026-01-01", Flat(100m))), D("2026-11-11"), D("2026-11-11"), [Sale(1, "2026-11-11", 150m)]);

        var total = SalesTargetPerformance.Aggregate([figures]);

        Assert.Equal((0m, 50m, 150m), (total.Shortfall, total.Exceeded, total.AchievementPercent));
    }

    [Fact]
    public void ClosedDay_TargetZero_NoShortfall_SalesStayVisible()
    {
        var figures = SalesTargetPerformance.ForShowroom(
            ScheduleFor(1, Version(1, "2026-01-01", Basundhara)), D("2026-11-10"), D("2026-11-10"), [Sale(1, "2026-11-10", 500m)]);

        var total = SalesTargetPerformance.Aggregate([figures]);

        Assert.Equal(0m, total.Target);
        Assert.Equal(MetricStatuses.OffDay, total.Status);
        Assert.Null(total.Shortfall);
        Assert.Null(total.Exceeded);
        Assert.Null(total.AchievementPercent);
        Assert.Equal(500m, figures.ComparableSales);
    }

    [Fact]
    public void Month_CountsOffDaySalesAgainstAWorkingDayTarget()
    {
        var figures = SalesTargetPerformance.ForShowroom(
            ScheduleFor(1, Version(1, "2026-01-01", Flat(100m, DayOfWeek.Tuesday))),
            D("2026-11-01"),
            D("2026-11-30"),
            [Sale(1, "2026-11-10", 500m), Sale(1, "2026-11-11", 100m)]);

        Assert.Equal(2_600m, figures.Period.Target);
        Assert.Equal(600m, figures.ComparableSales);
    }

    [Fact]
    public void Missing_IsNotConfiguredWithNoFigures()
    {
        var figures = SalesTargetPerformance.ForShowroom(
            OutletSalesTargetSchedule.Empty(1), D("2026-11-10"), D("2026-11-10"), [Sale(1, "2026-11-10", 500m)]);

        var total = SalesTargetPerformance.Aggregate([figures]);

        Assert.Null(total.Target);
        Assert.Null(total.AchievementPercent);
        Assert.Equal(MetricStatuses.NotConfigured, total.Status);
        Assert.False(total.Coverage.IsComplete);
    }

    [Fact]
    public void AllShowrooms_AddsEachShowroomsOwnTarget()
    {
        var sales = new[] { Sale(1, "2026-11-11", 50m), Sale(2, "2026-11-11", 300m) };

        var total = SalesTargetPerformance.Aggregate(
        [
            SalesTargetPerformance.ForShowroom(ScheduleFor(1, Version(1, "2026-01-01", Flat(100m))), D("2026-11-11"), D("2026-11-11"), sales),
            SalesTargetPerformance.ForShowroom(ScheduleFor(2, Version(1, "2026-01-01", Flat(200m))), D("2026-11-11"), D("2026-11-11"), sales)
        ]);

        Assert.Equal(300m, total.Target);
        Assert.Equal(350m, total.Target + total.Exceeded);
        Assert.True(total.Coverage.IsComplete);
    }

    [Fact]
    public void PartialCoverage_IsFlagged_AndComparesOnlyShowroomsWithATarget()
    {
        var sales = new[] { Sale(1, "2026-11-11", 50m), Sale(2, "2026-11-11", 9_000m) };

        var total = SalesTargetPerformance.Aggregate(
        [
            SalesTargetPerformance.ForShowroom(ScheduleFor(1, Version(1, "2026-01-01", Flat(100m))), D("2026-11-11"), D("2026-11-11"), sales),
            SalesTargetPerformance.ForShowroom(OutletSalesTargetSchedule.Empty(2), D("2026-11-11"), D("2026-11-11"), sales)
        ]);

        Assert.Equal(MetricStatuses.Partial, total.Status);
        Assert.Equal(100m, total.Target);
        Assert.Equal(50m, total.AchievementPercent);
        Assert.False(total.Coverage.IsComplete);
        Assert.Equal(1, total.Coverage.NotConfiguredShowrooms);
        Assert.Contains("1 of 2", total.Note);
    }

    [Fact]
    public void TargetStartingMidPeriod_ComparesOnlyDaysThatHaveOne()
    {
        var figures = SalesTargetPerformance.ForShowroom(
            ScheduleFor(1, Version(1, "2026-11-11", Flat(100m))),
            D("2026-11-07"),
            D("2026-11-13"),
            [Sale(1, "2026-11-08", 9_000m), Sale(1, "2026-11-11", 100m)]);

        Assert.Equal(MetricStatuses.Partial, figures.Status);
        Assert.Equal(300m, figures.Period.Target);
        Assert.Equal(100m, figures.ComparableSales);
    }

    [Fact]
    public void ZeroTargetOnAnOpenDay_HasNoPercentage()
    {
        var figures = SalesTargetPerformance.ForShowroom(
            ScheduleFor(1, Version(1, "2026-01-01", Flat(0m))), D("2026-11-11"), D("2026-11-11"), [Sale(1, "2026-11-11", 10m)]);

        var total = SalesTargetPerformance.Aggregate([figures]);

        Assert.Equal(0m, total.Target);
        Assert.Null(total.AchievementPercent);
        Assert.Equal(MetricStatuses.Ok, total.Status);
    }
}
