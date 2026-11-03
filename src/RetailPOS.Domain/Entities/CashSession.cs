namespace RetailPOS.Core.Entities;

public class CashSession
{
    public long Id { get; set; }
    public long? BusinessId { get; set; }
    public long OutletId { get; set; }
    public long TerminalId { get; set; }

    /// <summary>The opening local calendar date in <see cref="TimeZoneIdSnapshot"/> (D9). PostgreSQL <c>date</c>; immutable.</summary>
    public DateTime BusinessDate { get; set; }

    /// <summary>The server-generated opening instant (UTC).</summary>
    public DateTime OpenedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public decimal OpeningCashAmount { get; set; }
    public decimal? ExpectedClosingCashAmount { get; set; }
    public decimal? ActualClosingCashAmount { get; set; }
    public decimal? DifferenceAmount { get; set; }
    public string Status { get; set; } = CashSessionStatuses.Open;
    public long OpenedByUserId { get; set; }
    public long? ClosedByUserId { get; set; }
    public string? OpeningNotes { get; set; }
    public string? ClosingNotes { get; set; }

    /// <summary>The effective IANA time zone resolved when the session opened (Outlet override, else Business default).</summary>
    public string TimeZoneIdSnapshot { get; set; } = "UTC";

    /// <summary>The maximum selling duration that applied when the session opened (D3); never changes afterwards.</summary>
    public int DurationSnapshotHours { get; set; } = BusinessPosSettings.MaxCashSessionDurationHoursLimit;

    /// <summary>Client key that makes opening idempotent per outlet.</summary>
    public string? OpenIdempotencyKey { get; set; }

    public string? VarianceReason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public virtual Business? Business { get; set; }
    public virtual Outlet Outlet { get; set; } = null!;
    public virtual PosTerminal Terminal { get; set; } = null!;
    public virtual User OpenedByUser { get; set; } = null!;
    public virtual User? ClosedByUser { get; set; }
    public virtual ICollection<Sale> Sales { get; set; } = new List<Sale>();

    /// <summary>The authoritative UTC instant from which no sale may complete (D4). Overdue is derived, never stored.</summary>
    public DateTime CutoffAtUtc => UtcTime.Normalize(OpenedAt).AddHours(DurationSnapshotHours);

    public bool IsOverdueAt(DateTime utcNow) =>
        Status == CashSessionStatuses.Open && UtcTime.Normalize(utcNow) >= CutoffAtUtc;
}
