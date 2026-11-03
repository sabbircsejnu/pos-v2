using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Core.Entities;

namespace RetailPOS.Application.Sales.FinancialPostings;

public static class SaleFinancialPostingEventTypes
{
    public const string Requested = "pos.sale.financial-posting.requested";
    public const string Changed = "pos.sale.financial-posting.changed";
}

/// <summary>Worker and retry settings (configuration section <c>FinancialPosting:Sales</c>).</summary>
public sealed class SaleFinancialPostingOptions
{
    public bool WorkerEnabled { get; set; } = true;
    public int PollIntervalSeconds { get; set; } = 5;
    public int BatchSize { get; set; } = 25;
    public int BaseRetrySeconds { get; set; } = 30;
    public int MaxRetrySeconds { get; set; } = 3600;

    /// <summary>How long a reversal waits before re-checking that the sale's own journal has posted.</summary>
    public int AwaitingSaleRetrySeconds { get; set; } = 30;

    public void Validate()
    {
        if (PollIntervalSeconds is < 1 or > 3600)
            throw new InvalidOperationException("FinancialPosting:Sales:PollIntervalSeconds must be between 1 and 3600.");
        if (BatchSize is < 1 or > 500)
            throw new InvalidOperationException("FinancialPosting:Sales:BatchSize must be between 1 and 500.");
        if (BaseRetrySeconds < 1 || MaxRetrySeconds < BaseRetrySeconds)
            throw new InvalidOperationException("FinancialPosting:Sales retry seconds must satisfy 1 <= base <= max.");
        if (AwaitingSaleRetrySeconds < 1)
            throw new InvalidOperationException("FinancialPosting:Sales:AwaitingSaleRetrySeconds must be positive.");
    }

    public TimeSpan Backoff(int attempts) =>
        TimeSpan.FromSeconds(Math.Min(MaxRetrySeconds, BaseRetrySeconds * Math.Pow(2, Math.Clamp(attempts - 1, 0, 20))));
}

/// <summary>
/// Writes the posting intent and its outbox event into the caller's open sale/void/refund transaction. Nothing is
/// posted to the General Ledger here (D10).
/// </summary>
public interface ISaleFinancialPostingWriter
{
    Task<SaleFinancialPosting> AppendSaleAsync(Sale sale, CancellationToken cancellationToken);

    Task<SaleFinancialPosting> AppendReversalAsync(
        Sale sale,
        IReadOnlyList<SaleRefundLine> refundLines,
        string reversalType,
        long actorUserId,
        CancellationToken cancellationToken);

    /// <summary>Called after the source transaction committed: wakes the posting worker early. Never required.</summary>
    void NotifyCommitted();
}

public interface ISaleFinancialPostingTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);
    Task RollbackAsync(CancellationToken cancellationToken);
}

public sealed record SaleFinancialPostingDue(long Id, long BusinessId, long OutletId, long ActorUserId);

public sealed record SaleFinancialPostingSummary(
    int Pending,
    int Failed,
    int Posted,
    int AwaitingSalePosting,
    DateTime? OldestUnpostedCreatedAtUtc);

public interface ISaleFinancialPostingStore
{
    Task<ISaleFinancialPostingTransaction> BeginTransactionAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Locks the posting row <c>FOR UPDATE</c> inside the current transaction. With <paramref name="skipLocked"/> a row
    /// another worker holds is skipped (null) instead of waited for; the row lock is the claim and is released by
    /// commit, rollback or a dropped connection, so a crashed worker never strands a posting.
    /// </summary>
    Task<SaleFinancialPosting?> LockAsync(long postingId, bool skipLocked, CancellationToken cancellationToken);

    Task<SaleFinancialPosting?> FindAsync(long postingId, CancellationToken cancellationToken);

    /// <summary>Status of the sale's own (<c>kind = sale</c>) posting, or null when the sale has none (pre-D10 sale).</summary>
    Task<string?> FindSalePostingStatusAsync(long saleId, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<long, long?>> GetPaymentAccountsAsync(long saleId, CancellationToken cancellationToken);

    /// <summary>Records the resolved posting account on payments that have none yet (the pre-D10 behaviour).</summary>
    Task SnapshotPaymentAccountsAsync(
        long saleId,
        IReadOnlyDictionary<long, long> accounts,
        CancellationToken cancellationToken);

    /// <summary>Due postings of every Business, for the background worker only. Callers must scope each item.</summary>
    Task<IReadOnlyList<SaleFinancialPostingDue>> ListDueAsync(DateTime nowUtc, int take, CancellationToken cancellationToken);

    Task<IReadOnlyList<SaleFinancialPosting>> ListAsync(
        long businessId,
        string? status,
        int take,
        CancellationToken cancellationToken);

    Task<SaleFinancialPostingSummary> SummarizeAsync(long businessId, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);

    void DiscardPendingChanges();
}

public sealed record SaleJournalPostingResult(long JournalEntryId, IReadOnlyDictionary<long, long> PaymentAccounts);

/// <summary>
/// Builds the journal lines from an immutable snapshot and creates/posts it through the existing journal commands,
/// keyed by the sale so a retry resolves the same journal. Runs inside the posting transaction.
/// </summary>
public interface ISaleFinancialJournalPoster
{
    Task<SaleJournalPostingResult> PostSaleAsync(SaleJournalSnapshot snapshot, CancellationToken cancellationToken);

    Task<long> PostReversalAsync(
        SaleReversalJournalSnapshot snapshot,
        IReadOnlyDictionary<long, long?> paymentAccounts,
        CancellationToken cancellationToken);
}

public sealed record SaleFinancialPostingRetryAudit(
    long BusinessId,
    long PostingId,
    string SourceKey,
    string PreviousStatus,
    DateTime RequestedAtUtc,
    string Outcome,
    string NewStatus);

/// <summary>Durable record of an operator retry; the acting user comes from the request's audit context.</summary>
public interface ISaleFinancialPostingAuditWriter
{
    Task RecordRetryAsync(SaleFinancialPostingRetryAudit retry, CancellationToken cancellationToken);
}

/// <summary>Labels a posting failure for operators. Behaviour does not depend on the label: all failures retry.</summary>
public static class SaleFinancialPostingFailure
{
    private const int MaxErrorLength = 500;

    public static (string Code, string Message) Classify(Exception exception)
    {
        var message = Sanitize(exception);
        var text = exception.Message;
        string code;
        if (exception is SaleFinancialSnapshotException)
            code = SaleFinancialPostingErrorCodes.SnapshotMismatch;
        else if (exception is SaleFinancialEvidenceException evidence)
            code = evidence.Code;
        else if (exception is InvalidOperationException
                 && (Contains(text, "accounting period") || Contains(text, "financial year")
                     || Contains(text, "Only Open periods")))
            code = SaleFinancialPostingErrorCodes.AccountingPeriod;
        else if (exception is InvalidOperationException
                 && (Contains(text, "posting account") || Contains(text, "accounts were not found")
                     || Contains(text, "Inactive accounts") || Contains(text, "Outlet not found")
                     || Contains(text, "slot")))
            code = SaleFinancialPostingErrorCodes.AccountingConfiguration;
        else if (exception is InvalidOperationException or ArgumentException)
            code = SaleFinancialPostingErrorCodes.PostingRejected;
        else
            code = SaleFinancialPostingErrorCodes.Transient;
        return (code, message);
    }

    private static bool Contains(string text, string value) =>
        text.Contains(value, StringComparison.OrdinalIgnoreCase);

    // The root cause is kept because wrapper exceptions (EF parameter evaluation, MediatR) hide it.
    private static string Sanitize(Exception exception)
    {
        var root = exception.GetBaseException();
        var message = $"{exception.GetType().Name}: {exception.Message}";
        if (!ReferenceEquals(root, exception))
            message += $" <- {root.GetType().Name}: {root.Message}";
        message = message.ReplaceLineEndings(" ");
        return message.Length <= MaxErrorLength ? message : message[..MaxErrorLength];
    }
}

/// <summary>The stored snapshot no longer matches its commit-time hash; posting it would not reconcile to the sale.</summary>
public sealed class SaleFinancialSnapshotException(string message) : Exception(message);

/// <summary>
/// The immutable snapshot does not carry the financial evidence the journal needs (or it does not reconcile). The
/// posting fails with <see cref="Code"/> and stays visible; no amount, allocation or date is invented.
/// </summary>
public sealed class SaleFinancialEvidenceException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
