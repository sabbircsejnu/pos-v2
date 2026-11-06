namespace RetailPOS.Core.Entities;

/// <summary>
/// One effective-dated outlet schedule and target configuration (FR-TARGET-03). Rows are append-only so past reporting
/// never changes. The version in force on a date is the one with the latest EffectiveFrom on or before it, and the
/// highest VersionNo when one date was saved twice.
/// </summary>
public sealed class OutletSalesTargetVersion
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long OutletId { get; set; }
    public int VersionNo { get; set; }
    public DateOnly EffectiveFrom { get; set; }

    /// <summary>False keeps the weekly off days but means "no target": reports show N/A, never zero.</summary>
    public bool IsEnabled { get; set; }

    public long? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Outlet? Outlet { get; set; }
    public List<OutletSalesTargetVersionDay> Days { get; set; } = [];
}

/// <summary>One weekday of a version. A version always has seven rows, and an off day never has an amount.</summary>
public sealed class OutletSalesTargetVersionDay
{
    public long Id { get; set; }
    public long VersionId { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
    public bool IsOffDay { get; set; }
    public decimal? TargetAmount { get; set; }
}

/// <summary>
/// A manual amount for one exact calendar period. It replaces that period's calculated total and is never spread over
/// daily targets. Reset to Auto stamps ClearedAt, which keeps the row as history.
/// </summary>
public sealed class OutletSalesTargetOverride
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long OutletId { get; set; }
    public string PeriodType { get; set; } = string.Empty;
    public DateOnly PeriodFrom { get; set; }
    public DateOnly PeriodTo { get; set; }
    public decimal CalculatedAmount { get; set; }
    public decimal OverrideAmount { get; set; }
    public OutletSalesTargetVersion? Version { get; set; }
    public long VersionId { get; set; }
    public string? Reason { get; set; }
    public long? UpdatedByUserId { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ClearedAt { get; set; }
    public long? ClearedByUserId { get; set; }

    public Outlet? Outlet { get; set; }
}
