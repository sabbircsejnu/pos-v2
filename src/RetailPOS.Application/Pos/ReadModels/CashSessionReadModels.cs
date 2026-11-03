namespace RetailPOS.Application.Pos.ReadModels;

public sealed record CashSessionSnapshot(
    long Id,
    long? BusinessId,
    long OutletId,
    long TerminalId,
    DateTime BusinessDate,
    DateTime OpenedAt,
    DateTime? ClosedAt,
    decimal OpeningCashAmount,
    decimal? ExpectedClosingCashAmount,
    decimal? ActualClosingCashAmount,
    decimal? DifferenceAmount,
    string Status,
    long OpenedByUserId,
    long? ClosedByUserId,
    string? OpeningNotes,
    string? ClosingNotes,
    string TimeZoneId = "UTC",
    int DurationSnapshotHours = 20);

public sealed record CashSessionReadModel(
    long Id,
    long? BusinessId,
    long OutletId,
    long TerminalId,
    DateTime BusinessDate,
    DateTime OpenedAt,
    DateTime? ClosedAt,
    decimal OpeningCashAmount,
    decimal? ExpectedClosingCashAmount,
    decimal? ActualClosingCashAmount,
    decimal? DifferenceAmount,
    string Status,
    long OpenedByUserId,
    long? ClosedByUserId,
    string? OpeningNotes,
    string? ClosingNotes,
    bool IsOverdue,
    int MaxDurationHours,
    long ElapsedSeconds,
    string TimeZoneId = "UTC",
    DateTime? CutoffAtUtc = null,
    string WarningLevel = "none",
    string State = "open",
    DateTime? ServerNowUtc = null,
    string? VarianceReason = null);

/// <summary>One payment allocation of a sale; <see cref="NetCashRetained"/> is what actually stayed in the drawer (D14).</summary>
public sealed record CashSessionPaymentFact(
    string? Method,
    decimal Amount,
    decimal? Tendered,
    decimal? ChangeGiven,
    decimal? NetCashRetained);

public sealed record CashSessionSaleFact(
    long SaleId,
    string? Status,
    string? PaymentMethod,
    decimal TotalAmount,
    IReadOnlyList<CashSessionPaymentFact> Payments);

/// <summary>An append-only physical cash fact assigned to the session that received/dispensed the cash.</summary>
public sealed record CashSessionMovementFact(
    long Id,
    string MovementType,
    decimal CashEffect,
    string SourceType,
    long? SourceId,
    long? OriginalSaleId,
    long? OriginalCashSessionId,
    DateTime RecordedAtUtc);

public sealed record CashSessionFacts(
    CashSessionSnapshot Session,
    IReadOnlyList<CashSessionSaleFact> Sales,
    IReadOnlyList<CashSessionMovementFact> Movements);

public sealed record CashSessionTerminalOption(long Id, string Name, string Code, bool HasOpenSession);

/// <summary>Everything the Open Cash Session page shows read-only: the server-derived date, time zone and limit.</summary>
public sealed record CashSessionOpenContextReadModel(
    long OutletId,
    string OutletName,
    IReadOnlyList<CashSessionTerminalOption> Terminals,
    long? AutoSelectedTerminalId,
    string TimeZoneId,
    DateTime BusinessDate,
    DateTime ServerNowUtc,
    int MaxDurationHours);

public sealed record CashSessionCloseSummaryReadModel(
    long SessionId,
    long OutletId,
    long TerminalId,
    DateTime BusinessDate,
    DateTime OpenedAt,
    DateTime ClosingAt,
    long OpenDurationSeconds,
    decimal OpeningCash,
    decimal CashSales,
    decimal CashTendered,
    decimal ChangeGiven,
    decimal CardSales,
    decimal MobileSales,
    decimal OtherNonCashSales,
    decimal DrawerExpenses,
    decimal CashRefunds,
    decimal Corrections,
    decimal ExpectedClosingCash,
    int SaleCount,
    bool IsOverdue);

/// <summary>
/// Sales recorded on one outlet business date (Phase 4 Daily Sales card). Completed sales are totalled; voided and
/// refunded sales are only counted, because a refund marks the whole sale and the row carries no refunded amount.
/// </summary>
public sealed record BusinessDaySalesReadModel(
    long OutletId,
    string OutletName,
    DateTime BusinessDate,
    string TimeZoneId,
    DateTime ServerNowUtc,
    int CompletedCount,
    decimal CompletedTotal,
    decimal CompletedTax,
    decimal CompletedNetOfTax,
    decimal CompletedDiscount,
    int VoidedCount,
    int RefundedCount,
    decimal RefundedOriginalTotal);

public sealed record BusinessDaySalesTotals(
    int CompletedCount,
    decimal CompletedTotal,
    decimal CompletedTax,
    decimal CompletedDiscount,
    int VoidedCount,
    int RefundedCount,
    decimal RefundedOriginalTotal);
