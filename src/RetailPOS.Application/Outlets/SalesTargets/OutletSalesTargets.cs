namespace RetailPOS.Application.Outlets.SalesTargets;

public static class SalesTargetPeriodTypes
{
    public const string Weekly = "weekly";
    public const string FirstHalf = "first_half";
    public const string SecondHalf = "second_half";
    public const string Monthly = "monthly";

    public static readonly IReadOnlyList<string> All = [Weekly, FirstHalf, SecondHalf, Monthly];
}

public static class SalesTargetErrorCodes
{
    public const string InvalidConfiguration = "OUTLET_TARGET_INVALID";
    public const string AllDaysOff = "OUTLET_TARGET_ALL_DAYS_OFF";
    public const string InvalidEffectiveDate = "OUTLET_TARGET_EFFECTIVE_DATE_INVALID";
    public const string InvalidOverride = "OUTLET_TARGET_OVERRIDE_INVALID";
    public const string Conflict = "OUTLET_TARGET_CONFLICT";
}

// ---- Write input ----------------------------------------------------------------------------------------------------

public sealed record OutletSalesTargetDayInput(DayOfWeek DayOfWeek, bool IsOffDay, decimal? TargetAmount);

/// <param name="OverrideAmount">Null resets the period to its calculated amount (Reset to Auto).</param>
public sealed record OutletSalesTargetOverrideInput(
    string PeriodType,
    DateOnly PeriodFrom,
    decimal? OverrideAmount,
    string? Reason);

/// <param name="ExpectedVersionNo">The version the form loaded; null when it loaded none. A stale value is refused.</param>
public sealed record OutletSalesTargetInput(
    bool IsEnabled,
    DateOnly EffectiveFrom,
    IReadOnlyList<OutletSalesTargetDayInput> Days,
    IReadOnlyList<OutletSalesTargetOverrideInput>? Overrides,
    int? ExpectedVersionNo);

// ---- Calculation input ----------------------------------------------------------------------------------------------

public sealed record SalesTargetDaySnapshot(DayOfWeek DayOfWeek, bool IsOffDay, decimal? TargetAmount);

public sealed record SalesTargetVersionSnapshot(
    int VersionNo,
    DateOnly EffectiveFrom,
    bool IsEnabled,
    IReadOnlyList<SalesTargetDaySnapshot> Days);

public sealed record SalesTargetOverrideSnapshot(
    string PeriodType,
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    decimal OverrideAmount);

/// <summary>Everything needed to work out one outlet's targets for any date.</summary>
public sealed record OutletSalesTargetSchedule(
    long OutletId,
    IReadOnlyList<SalesTargetVersionSnapshot> Versions,
    IReadOnlyList<SalesTargetOverrideSnapshot> Overrides)
{
    public static OutletSalesTargetSchedule Empty(long outletId) => new(outletId, [], []);
}

// ---- Calculation results --------------------------------------------------------------------------------------------

public enum SalesTargetDayKind
{
    /// <summary>No version is in force, or targets are disabled: the target is N/A, not zero.</summary>
    NotConfigured,
    /// <summary>A scheduled off day: the target is zero and no shortfall applies.</summary>
    OffDay,
    Working
}

public sealed record SalesTargetDay(DateOnly Date, SalesTargetDayKind Kind, decimal? Amount);

/// <param name="Target">The amount in force: the override when one matches this exact range, else the calculated sum.</param>
/// <param name="Calculated">Sum of the daily targets, ignoring overrides. Null when no day of the range is configured.</param>
public sealed record SalesTargetPeriodResult(
    DateOnly From,
    DateOnly To,
    decimal? Target,
    decimal? Calculated,
    bool IsOverridden,
    int WorkingDays,
    int OffDays,
    int NotConfiguredDays);

// ---- Read models ----------------------------------------------------------------------------------------------------

public sealed record OutletSalesTargetVersionReadModel(
    long Id,
    int VersionNo,
    DateOnly EffectiveFrom,
    bool IsEnabled,
    IReadOnlyList<SalesTargetDaySnapshot> Days,
    long? CreatedByUserId,
    DateTime CreatedAt);

public sealed record OutletSalesTargetPeriodReadModel(
    string PeriodType,
    DateOnly From,
    DateOnly To,
    decimal? Calculated,
    decimal? Target,
    decimal? OverrideAmount,
    bool IsOverridden,
    string? Reason,
    long? UpdatedByUserId,
    DateTime? UpdatedAt,
    int WorkingDays,
    int OffDays,
    int NotConfiguredDays);

/// <param name="Latest">The newest saved version, which the form edits; it may start in the future.</param>
/// <param name="Current">The version in force on <paramref name="Today"/>; null means not configured yet.</param>
public sealed record OutletSalesTargetConfigurationReadModel(
    long OutletId,
    DateOnly Today,
    OutletSalesTargetVersionReadModel? Latest,
    OutletSalesTargetVersionReadModel? Current,
    IReadOnlyList<OutletSalesTargetPeriodReadModel> Periods,
    IReadOnlyList<OutletSalesTargetVersionReadModel> History);

public sealed record OutletSalesTargetStoredOverride(
    string PeriodType,
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    decimal OverrideAmount,
    string? Reason,
    long? UpdatedByUserId,
    DateTime UpdatedAt);

/// <summary>Stored rows of one outlet, for the configuration screen.</summary>
public sealed record OutletSalesTargetStoredConfiguration(
    long OutletId,
    long? BusinessId,
    IReadOnlyList<OutletSalesTargetVersionReadModel> Versions,
    IReadOnlyList<OutletSalesTargetStoredOverride> ActiveOverrides);

// ---- Ports ----------------------------------------------------------------------------------------------------------

public interface IOutletSalesTargetReadStore
{
    /// <summary>Outlets of the Business only; an outlet with no rows gets an empty schedule.</summary>
    Task<IReadOnlyDictionary<long, OutletSalesTargetSchedule>> GetSchedulesAsync(
        long businessId,
        IReadOnlyCollection<long> outletIds,
        CancellationToken cancellationToken);

    /// <summary>Null when the outlet is not in the Business.</summary>
    Task<OutletSalesTargetStoredConfiguration?> GetConfigurationAsync(
        long? businessId,
        long outletId,
        CancellationToken cancellationToken);
}
