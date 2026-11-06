namespace RetailPOS.Application.Outlets.SalesTargets;

/// <summary>
/// The one definition of an outlet's sales target for a date or period, used by the outlet form, the Owner Dashboard and
/// Cash Sessions. Side-effect free. Every period total walks actual calendar dates, so month lengths, leap years and
/// mid-period schedule changes need no special case, and nothing is approximated from a weekly figure.
/// </summary>
public static class SalesTargetCalculator
{
    /// <summary>The version in force on a date. A later save for the same date wins.</summary>
    public static SalesTargetVersionSnapshot? VersionOn(OutletSalesTargetSchedule schedule, DateOnly date)
    {
        SalesTargetVersionSnapshot? best = null;
        foreach (var version in schedule.Versions)
        {
            if (version.EffectiveFrom > date)
                continue;
            if (best is null
                || version.EffectiveFrom > best.EffectiveFrom
                || (version.EffectiveFrom == best.EffectiveFrom && version.VersionNo > best.VersionNo))
                best = version;
        }

        return best;
    }

    public static SalesTargetDay Day(OutletSalesTargetSchedule schedule, DateOnly date)
    {
        var version = VersionOn(schedule, date);
        if (version is null || !version.IsEnabled)
            return new SalesTargetDay(date, SalesTargetDayKind.NotConfigured, null);

        var day = version.Days.FirstOrDefault(candidate => candidate.DayOfWeek == date.DayOfWeek);
        if (day is null)
            return new SalesTargetDay(date, SalesTargetDayKind.NotConfigured, null);
        if (day.IsOffDay)
            return new SalesTargetDay(date, SalesTargetDayKind.OffDay, 0m);

        return day.TargetAmount is null
            ? new SalesTargetDay(date, SalesTargetDayKind.NotConfigured, null)
            : new SalesTargetDay(date, SalesTargetDayKind.Working, day.TargetAmount);
    }

    /// <summary>
    /// A range's target. An override applies only when the range is exactly its period, and then replaces the total, so
    /// it can never be counted twice. Any other range uses the daily calculation. An override never revives a period in
    /// which targets were disabled or not yet configured.
    /// </summary>
    public static SalesTargetPeriodResult Period(OutletSalesTargetSchedule schedule, DateOnly from, DateOnly to)
    {
        var calculated = 0m;
        var working = 0;
        var off = 0;
        var missing = 0;
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            var day = Day(schedule, date);
            switch (day.Kind)
            {
                case SalesTargetDayKind.Working:
                    working++;
                    calculated += day.Amount!.Value;
                    break;
                case SalesTargetDayKind.OffDay:
                    off++;
                    break;
                default:
                    missing++;
                    break;
            }
        }

        decimal? calculatedTotal = working + off > 0 ? calculated : null;
        var match = calculatedTotal is null
            ? null
            : schedule.Overrides.FirstOrDefault(item => item.PeriodFrom == from && item.PeriodTo == to);
        return new SalesTargetPeriodResult(
            from,
            to,
            match?.OverrideAmount ?? calculatedTotal,
            calculatedTotal,
            match is not null,
            working,
            off,
            missing);
    }

    /// <summary>The Saturday-to-Friday business week containing a date.</summary>
    public static (DateOnly From, DateOnly To) WeekOf(DateOnly date)
    {
        var daysSinceSaturday = ((int)date.DayOfWeek + 1) % 7;
        var from = date.AddDays(-daysSinceSaturday);
        return (from, from.AddDays(6));
    }

    public static (DateOnly From, DateOnly To) FirstHalfOf(DateOnly date) =>
        (new DateOnly(date.Year, date.Month, 1), new DateOnly(date.Year, date.Month, 15));

    public static (DateOnly From, DateOnly To) SecondHalfOf(DateOnly date) =>
        (new DateOnly(date.Year, date.Month, 16), LastDayOf(date));

    public static (DateOnly From, DateOnly To) MonthOf(DateOnly date) =>
        (new DateOnly(date.Year, date.Month, 1), LastDayOf(date));

    /// <summary>The calendar range of a period type that contains a date.</summary>
    public static (DateOnly From, DateOnly To) RangeOf(string periodType, DateOnly date) => periodType switch
    {
        SalesTargetPeriodTypes.Weekly => WeekOf(date),
        SalesTargetPeriodTypes.FirstHalf => FirstHalfOf(date),
        SalesTargetPeriodTypes.SecondHalf => SecondHalfOf(date),
        SalesTargetPeriodTypes.Monthly => MonthOf(date),
        _ => throw new ArgumentOutOfRangeException(nameof(periodType), periodType, "Unknown target period type.")
    };

    private static DateOnly LastDayOf(DateOnly date) =>
        new(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month));
}

/// <summary>Input rules for a target save. Date rules that need stored rows are checked by the applier.</summary>
public static class SalesTargetRules
{
    public const int MaxReasonLength = 500;

    /// <summary>numeric(18,2): at most 16 integer digits.</summary>
    private const decimal MaxAmount = 9_999_999_999_999_999.99m;

    public static void Validate(OutletSalesTargetInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (input.Days is null
            || input.Days.Count != 7
            || input.Days.Select(day => day.DayOfWeek).Distinct().Count() != 7
            || input.Days.Any(day => !Enum.IsDefined(day.DayOfWeek)))
            throw Invalid("Send exactly one entry for each of the seven weekdays.");

        if (input.Days.All(day => day.IsOffDay))
            throw new OutletSetupException(
                SalesTargetErrorCodes.AllDaysOff,
                "An outlet cannot be closed on all seven days. Leave at least one working day.");

        foreach (var day in input.Days)
        {
            if (day.TargetAmount is null)
            {
                if (input.IsEnabled && !day.IsOffDay)
                    throw Invalid($"Enter a target for {day.DayOfWeek}.");
                continue;
            }

            RequireAmount(day.TargetAmount.Value, day.DayOfWeek.ToString(), SalesTargetErrorCodes.InvalidConfiguration);
        }

        foreach (var item in input.Overrides ?? [])
            ValidateOverride(item);
    }

    /// <summary>Weekdays that are off carry no amount, so reopening one can never bring back an old target unseen.</summary>
    public static IReadOnlyList<SalesTargetDaySnapshot> Normalize(IReadOnlyList<OutletSalesTargetDayInput> days) =>
        days
            .Select(day => new SalesTargetDaySnapshot(day.DayOfWeek, day.IsOffDay, day.IsOffDay ? null : day.TargetAmount))
            .OrderBy(day => day.DayOfWeek)
            .ToList();

    public static bool SameConfiguration(
        SalesTargetVersionSnapshot version,
        bool isEnabled,
        IReadOnlyList<SalesTargetDaySnapshot> days) =>
        version.IsEnabled == isEnabled
        && version.Days.OrderBy(day => day.DayOfWeek).SequenceEqual(days);

    /// <summary>The exact calendar range an override must cover; a period start inside the period is refused.</summary>
    public static (DateOnly From, DateOnly To) RequirePeriod(OutletSalesTargetOverrideInput item)
    {
        if (!SalesTargetPeriodTypes.All.Contains(item.PeriodType))
            throw new OutletSetupException(SalesTargetErrorCodes.InvalidOverride, "Unknown target period.");

        var range = SalesTargetCalculator.RangeOf(item.PeriodType, item.PeriodFrom);
        if (range.From != item.PeriodFrom)
            throw new OutletSetupException(
                SalesTargetErrorCodes.InvalidOverride,
                "The period must start on its first day: Saturday for a week, the 1st or the 16th for a half month, and the 1st for a month.");
        return range;
    }

    private static void ValidateOverride(OutletSalesTargetOverrideInput item)
    {
        RequirePeriod(item);

        if (item.OverrideAmount is not null)
            RequireAmount(item.OverrideAmount.Value, "The override", SalesTargetErrorCodes.InvalidOverride);

        if (item.Reason?.Trim().Length > MaxReasonLength)
            throw new OutletSetupException(
                SalesTargetErrorCodes.InvalidOverride,
                $"The override reason must be at most {MaxReasonLength} characters.");
    }

    private static void RequireAmount(decimal amount, string label, string code)
    {
        if (amount < 0m)
            throw new OutletSetupException(code, $"{label}: a target cannot be negative.");
        if (amount > MaxAmount)
            throw new OutletSetupException(code, $"{label}: the target is too large.");
        if (decimal.Round(amount, 2) != amount)
            throw new OutletSetupException(code, $"{label}: use at most two decimal places.");
    }

    private static OutletSetupException Invalid(string message) =>
        new(SalesTargetErrorCodes.InvalidConfiguration, message);
}
