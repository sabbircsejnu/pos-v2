namespace RetailPOS.Core.Entities;

public static class SaleFinancialPostingKinds
{
    /// <summary>The revenue/payment/COGS journal of a completed sale.</summary>
    public const string Sale = "sale";

    /// <summary>The single reversal journal of a voided or refunded sale.</summary>
    public const string Reversal = "reversal";
}

public static class SaleFinancialPostingReversalTypes
{
    public const string Void = "void";
    public const string Refund = "refund";
}

/// <summary>
/// D10 posting state machine: <c>pending → posted</c> or <c>pending → failed → (retry) → posted</c>. <c>posted</c> is
/// terminal and enforced by the database. A failed posting keeps being retried with backoff; it is never dropped.
/// </summary>
public static class SaleFinancialPostingStatuses
{
    public const string Pending = "pending";
    public const string Posted = "posted";
    public const string Failed = "failed";
}

/// <summary>Classification of the last failure. It only labels the failure; every class stays retryable.</summary>
public static class SaleFinancialPostingErrorCodes
{
    public const string AccountingConfiguration = "accounting_configuration";
    public const string AccountingPeriod = "accounting_period";
    public const string AwaitingSalePosting = "awaiting_sale_posting";
    public const string SnapshotMismatch = "snapshot_mismatch";
    public const string Transient = "transient";
    public const string PostingRejected = "posting_rejected";

    /// <summary>The persisted sale lacks the line/payment allocations a reversal needs; nothing is guessed (P3.2-D01).</summary>
    public const string AllocationEvidenceMissing = "allocation_evidence_missing";

    /// <summary>The snapshot's payment allocations do not equal its payable; the journal would not balance.</summary>
    public const string PaymentAllocationMismatch = "payment_allocation_mismatch";

    /// <summary>A pre-3.2 (v1) reversal snapshot: no accounting date or VAT allocation; held for review, never posted.</summary>
    public const string SnapshotRequiresReview = "snapshot_requires_review";
}

/// <summary>
/// The durable, immutable financial-posting intent of a sale or its reversal (LLD §20.3 <c>sale_financial_postings</c>).
/// It is written in the same transaction as the sale facts and the outbox event, and the General Ledger journal is
/// produced from <see cref="SnapshotJson"/> after that transaction has committed. Identity, snapshot and hash never
/// change after insert (database trigger); only the posting state columns move.
/// </summary>
public class SaleFinancialPosting
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long SaleId { get; set; }
    public string Kind { get; set; } = SaleFinancialPostingKinds.Sale;
    public string? ReversalType { get; set; }

    /// <summary>The refund transaction behind this reversal. Null for voids and for refunds recorded before multiple refunds.</summary>
    public long? SaleRefundId { get; set; }

    /// <summary>Stable posting intent key, e.g. <c>sale:42:posting:v1</c>; unique per Business.</summary>
    public string SourceKey { get; set; } = string.Empty;

    public string SnapshotJson { get; set; } = string.Empty;
    public string SnapshotHash { get; set; } = string.Empty;

    public long OutletId { get; set; }
    public long? TerminalId { get; set; }
    public long? CashSessionId { get; set; }
    public long ActorUserId { get; set; }

    /// <summary>The sale's business date (calendar date). The journal is never re-dated to a different period.</summary>
    public DateTime BusinessDate { get; set; }

    public string Status { get; set; } = SaleFinancialPostingStatuses.Pending;
    public int Attempts { get; set; }
    public string? LastErrorCode { get; set; }
    public string? LastError { get; set; }
    public DateTime? NextAttemptAtUtc { get; set; }
    public DateTime? LastAttemptAtUtc { get; set; }
    public long? JournalEntryId { get; set; }
    public DateTime? PostedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public virtual Sale Sale { get; set; } = null!;
}
