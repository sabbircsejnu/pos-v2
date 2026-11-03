using RetailPOS.Application.Pos.Abstractions;
using RetailPOS.Application.Pos.ReadModels;
using RetailPOS.Core.Entities;

namespace RetailPOS.Application.Pos;

public static class CashSessionMapper
{
    public static CashDifferenceApprovalReadModel ToApprovalModel(CashDifferenceApproval approval) =>
        new(
            approval.Id,
            approval.CashSessionId,
            approval.VarianceAmount,
            approval.Reason,
            approval.Status,
            approval.PostingStatus,
            approval.PostingAttempts,
            approval.LastPostingError,
            approval.JournalEntryId,
            UtcTime.Normalize(approval.CreatedAtUtc),
            approval.CreatedByUserId,
            UtcTime.Normalize(approval.ReviewedAtUtc),
            approval.ReviewedByUserId,
            approval.ReviewNotes,
            UtcTime.Normalize(approval.PostedAtUtc));

    public static CashSessionSnapshot ToSnapshot(CashSession session) =>
        new(
            session.Id,
            session.BusinessId,
            session.OutletId,
            session.TerminalId,
            session.BusinessDate.Date,
            UtcTime.Normalize(session.OpenedAt),
            UtcTime.Normalize(session.ClosedAt),
            session.OpeningCashAmount,
            session.ExpectedClosingCashAmount,
            session.ActualClosingCashAmount,
            session.DifferenceAmount,
            session.Status,
            session.OpenedByUserId,
            session.ClosedByUserId,
            session.OpeningNotes,
            session.ClosingNotes,
            session.TimeZoneIdSnapshot,
            session.DurationSnapshotHours);

    /// <summary>The derived lifecycle state: <c>open</c>, <c>overdue</c> (derived, never stored) or <c>closed</c>.</summary>
    public static string StateOf(CashSession session, DateTime nowUtc) =>
        session.Status == CashSessionStatuses.Closed
            ? CashSessionStatuses.Closed
            : session.IsOverdueAt(nowUtc) ? "overdue" : CashSessionStatuses.Open;

    public static CashSessionReadModel ToReadModel(CashSession session, DateTime nowUtc)
    {
        var now = UtcTime.Normalize(nowUtc);
        var state = StateOf(session, now);
        var level = session.Status == CashSessionStatuses.Closed
            ? CashSessionWarningLevel.None
            : CashSessionTime.WarningLevel(session.OpenedAt, session.DurationSnapshotHours, now);
        var elapsedUntil = session.ClosedAt.HasValue ? UtcTime.Normalize(session.ClosedAt.Value) : now;
        return new CashSessionReadModel(
            session.Id,
            session.BusinessId,
            session.OutletId,
            session.TerminalId,
            session.BusinessDate.Date,
            UtcTime.Normalize(session.OpenedAt),
            UtcTime.Normalize(session.ClosedAt),
            session.OpeningCashAmount,
            session.ExpectedClosingCashAmount,
            session.ActualClosingCashAmount,
            session.DifferenceAmount,
            session.Status,
            session.OpenedByUserId,
            session.ClosedByUserId,
            session.OpeningNotes,
            session.ClosingNotes,
            state == "overdue",
            session.DurationSnapshotHours,
            CashSessionTime.ElapsedSeconds(session.OpenedAt, elapsedUntil),
            session.TimeZoneIdSnapshot,
            session.CutoffAtUtc,
            level.ToString().ToLowerInvariant(),
            state,
            now,
            session.VarianceReason);
    }
}
