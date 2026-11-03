using RetailPOS.Application.Pos.ReadModels;
using RetailPOS.Core.Entities;

namespace RetailPOS.Application.Pos.Abstractions;

public enum CashSessionLockMode
{
    /// <summary>Taken by sale completion and cash movements; compatible with other shared locks, conflicts with close.</summary>
    Share,

    /// <summary>Taken by close before the authoritative closing summary is computed.</summary>
    Update
}

public enum CashSessionAddResult
{
    Created,
    TerminalAlreadyOpen,
    IdempotencyReplay
}

public sealed record CashSessionTerminalInfo(long Id, string Name, string Code);

public sealed record CashSessionOpenContext(
    long BusinessId,
    string? OutletTimeZoneId,
    IReadOnlyList<CashSessionTerminalInfo> ActiveTerminals,
    string OutletName = "",
    IReadOnlyList<long>? TerminalIdsWithOpenSession = null);

public interface ICashSessionTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);

    Task RollbackAsync(CancellationToken cancellationToken);
}

public sealed record CashSessionClosePersist(
    long SessionId,
    decimal ExpectedClosingCash,
    decimal CountedCash,
    decimal Variance,
    DateTime ClosedAtUtc,
    long ClosedByUserId,
    string? ClosingNotes,
    string? VarianceReason);

/// <summary>
/// Persistence for the Cash Session aggregate. All locking is PostgreSQL row-level on the session row; the
/// deterministic order is always <b>session → (sale | expense | movement source key) → inventory</b>.
/// </summary>
public interface ICashSessionStore
{
    Task<ICashSessionTransaction> BeginTransactionAsync(CancellationToken cancellationToken);

    Task<long?> GetOutletIdAsync(long sessionId, CancellationToken cancellationToken);

    Task<CashSessionOpenContext?> GetOpenContextAsync(long outletId, CancellationToken cancellationToken);

    Task<CashSession?> FindByIdempotencyKeyAsync(long outletId, string key, CancellationToken cancellationToken);

    Task<long?> FindOpenSessionIdAsync(long outletId, long terminalId, CancellationToken cancellationToken);

    Task<IReadOnlyList<long>> ListOpenSessionIdsAsync(long outletId, CancellationToken cancellationToken);

    /// <summary>A fresh, untracked read without locking.</summary>
    Task<CashSession?> FindAsync(long sessionId, CancellationToken cancellationToken);

    /// <summary>
    /// A fresh, untracked read that takes the row lock (<c>FOR SHARE</c>/<c>FOR UPDATE</c>) inside the current
    /// transaction. Callers must re-check status and the UTC cutoff on the returned row.
    /// </summary>
    Task<CashSession?> LockAsync(long sessionId, CashSessionLockMode mode, CancellationToken cancellationToken);

    Task<CashSessionAddResult> AddOpenSessionAsync(CashSession session, CancellationToken cancellationToken);

    Task<CashSessionFacts?> GetFactsAsync(long sessionId, CancellationToken cancellationToken);

    /// <summary>Conditional <c>UPDATE … WHERE status = 'open'</c>; false when the session was not open.</summary>
    Task<bool> MarkClosedAsync(CashSessionClosePersist close, CancellationToken cancellationToken);

    Task AddCloseEvidenceAsync(
        CashSessionCloseSnapshot snapshot,
        IReadOnlyList<CashSessionHandover> handovers,
        CashDifferenceApproval? approval,
        CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public sealed record CashSessionMovementRequest(
    long CashSessionId,
    string MovementType,
    decimal CashEffect,
    string SourceType,
    long? SourceId,
    string SourceKey,
    long? OriginalSaleId,
    long? OriginalCashSessionId,
    string? Notes,
    long ActorUserId);

public sealed record CashSessionMovementResult(CashSessionMovement Movement, bool Created);

/// <summary>
/// Records an exactly-once physical cash effect on the dispensing/receiving session. It must be called inside the
/// caller's transaction; it takes the shared session lock itself and re-checks status and the UTC cutoff.
/// </summary>
public interface ICashSessionMovementRecorder
{
    Task<CashSessionMovementResult> RecordAsync(
        CashSessionMovementRequest request,
        CancellationToken cancellationToken);
}

public sealed record CashSessionEvent(
    string MessageType,
    long BusinessId,
    string AggregateType,
    string AggregateId,
    string Discriminator,
    DateTime OccurredAtUtc,
    object Payload);

public static class CashSessionEventTypes
{
    public const string Opened = "pos.cash-session.opened";
    public const string Closed = "pos.cash-session.closed";
    public const string MovementRecorded = "pos.cash-session.movement-recorded";
    public const string DrawerExpensePaid = "pos.cash-session.drawer-expense-paid";
    public const string DrawerExpenseReversed = "pos.cash-session.drawer-expense-reversed";
    public const string VarianceRaised = "pos.cash-difference.raised";
    public const string VarianceDecided = "pos.cash-difference.decided";
    public const string VariancePostingChanged = "pos.cash-difference.posting-changed";
    public const string HandoverConfirmed = "pos.cash-session.handover-confirmed";
}

/// <summary>Appends a durable integration-outbox event inside the caller's open database transaction.</summary>
public interface ICashSessionEventWriter
{
    Task AppendAsync(CashSessionEvent cashSessionEvent, CancellationToken cancellationToken);
}

public interface ICashSessionAuditWriter
{
    Task RecordAsync(
        string action,
        string summary,
        long cashSessionId,
        IReadOnlyDictionary<string, object?> metadata,
        CancellationToken cancellationToken);
}

public interface ICashSessionOutletAuthorizer
{
    Task<long> AuthorizeReadAsync(long outletId, CancellationToken cancellationToken);
}

public interface ICashSessionWriteOutletAuthorizer
{
    Task<long> AuthorizeWriteAsync(long outletId, CancellationToken cancellationToken);
}

public interface ICashSessionOpenAuditWriter
{
    Task RecordOpenedAsync(
        CashSessionSnapshot session,
        decimal openingCashAmount,
        CancellationToken cancellationToken);
}

public interface ICashSessionOpenedPublisher
{
    Task PublishOpenedAsync(CashSessionSnapshot session, CancellationToken cancellationToken);
}

public interface ICashSessionCloseAuditWriter
{
    Task RecordClosedAsync(
        CashSessionSnapshot session,
        decimal expectedClosingCash,
        decimal actualClosingCash,
        decimal difference,
        long? varianceApprovalId,
        CancellationToken cancellationToken);
}

public interface ICashSessionClosedPublisher
{
    Task PublishClosedAsync(
        CashSessionSnapshot session,
        long closedByUserId,
        decimal difference,
        CancellationToken cancellationToken);
}

public sealed record CashSessionVarianceJournalRequest(
    long ApprovalId,
    long SessionId,
    long BusinessId,
    long OutletId,
    long TerminalId,
    DateTime BusinessDate,
    DateTime TransactionTimestamp,
    decimal Difference,
    long ActorUserId);

public interface ICashSessionVarianceJournalPoster
{
    /// <summary>Creates (idempotently, keyed by the approval) and posts the cash-difference journal.</summary>
    Task<long?> PostAsync(
        CashSessionVarianceJournalRequest request,
        CancellationToken cancellationToken);
}
