using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Exchanges;

namespace RetailPOS.Application.Exchanges.Abstractions;

public interface IExchangeTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);

    /// <summary>Rolls back and clears the change tracker: early SaveChanges by shared services already accepted entities (Stage 0 §4.3).</summary>
    Task RollbackAsync(CancellationToken cancellationToken);
}

public sealed record ExchangeAccountingPeriodState(bool Exists, bool IsOpen, string? Name, string? Status);

/// <summary>One stock ledger row an exchange wrote at its receiving outlet; a reversal writes its exact mirror.</summary>
public sealed record ExchangeStockMovement(long Id, long VariantId, decimal QtyIn, decimal QtyOut, decimal UnitCost, decimal MovementValue);

/// <summary>Persistence and locking for product exchanges. Every method is business-scoped by its caller's arguments.</summary>
public interface IExchangeStore
{
    /// <summary>Joins an ambient transaction (no-op handle) or opens a Read Committed one.</summary>
    Task<IExchangeTransaction> BeginTransactionAsync(CancellationToken cancellationToken);

    Task<ExchangePolicyVersion?> FindCurrentPolicyAsync(long businessId, CancellationToken cancellationToken);
    Task AddPolicyAsync(ExchangePolicyVersion policy, CancellationToken cancellationToken);

    Task<long?> FindSaleIdByInvoiceNumberAsync(long businessId, string invoiceNumber, CancellationToken cancellationToken);

    /// <summary>Loads sales of the business with items, components and customer. With <paramref name="forUpdate"/> the sale rows are locked in ascending id order first.</summary>
    Task<IReadOnlyList<Sale>> LoadSourceSalesAsync(long businessId, IReadOnlyCollection<long> saleIds, bool forUpdate, CancellationToken cancellationToken);

    Task<IReadOnlyList<ExchangeReplacementLine>> LoadReplacementLinesAsync(long businessId, IReadOnlyCollection<long> lineIds, bool forUpdate, CancellationToken cancellationToken);

    Task<long?> FindWalkInCustomerIdAsync(long businessId, CancellationToken cancellationToken);

    /// <summary>Unlocked stock read for preview.</summary>
    Task<IReadOnlyDictionary<long, decimal>> ReadOutletStockAsync(long outletId, IReadOnlyCollection<long> variantIds, CancellationToken cancellationToken);

    /// <summary>Locks every existing receiving-outlet inventory row of the variants in one statement, ordered by id (Stage 0 §7.2).</summary>
    Task<Dictionary<long, RetailPOS.Core.Entities.Inventory>> LockOutletInventoriesAsync(long outletId, IReadOnlyCollection<long> variantIds, CancellationToken cancellationToken);

    /// <summary>Creates a missing outlet inventory row; a concurrent creation surfaces as a retryable conflict.</summary>
    Task<RetailPOS.Core.Entities.Inventory> CreateOutletInventoryAsync(long variantId, long outletId, CancellationToken cancellationToken);

    Task<string?> FindOutletInvoicePrefixAsync(long outletId, CancellationToken cancellationToken);
    Task<long> AllocateExchangeSequenceAsync(long businessId, long outletId, DateTime businessDate, CancellationToken cancellationToken);

    /// <summary>Locks the period row covering the date FOR SHARE so a concurrent lock/close waits (VI-18).</summary>
    Task<ExchangeAccountingPeriodState> LockAccountingPeriodAsync(long businessId, DateTime businessDate, CancellationToken cancellationToken);

    Task<IReadOnlyList<ExchangeApproval>> LockApprovalsAsync(long businessId, IReadOnlyCollection<string> tokenHashes, CancellationToken cancellationToken);
    Task AddApprovalsAsync(IReadOnlyCollection<ExchangeApproval> approvals, CancellationToken cancellationToken);
    /// <summary>L1b: failed approval attempts of one subject since a moment (business-scoped).</summary>
    Task<int> CountApprovalFailuresAsync(long businessId, string subjectKind, string subjectHash, DateTime sinceUtc, CancellationToken cancellationToken);
    /// <summary>L1b: records failed attempts and commits them at once (they must survive the refusal that follows).</summary>
    Task RecordApprovalFailuresAsync(IReadOnlyCollection<ExchangeApprovalFailure> failures, CancellationToken cancellationToken);
    /// <summary>L1b: a successful approval clears the subjects' failures.</summary>
    Task ClearApprovalFailuresAsync(long businessId, IReadOnlyCollection<string> subjectHashes, CancellationToken cancellationToken);

    /// <summary>Serialises every cash outflow of one drawer; always the last lock taken (Stage 0 §7.2).</summary>
    Task AcquireDrawerLockAsync(long businessId, long cashSessionId, CancellationToken cancellationToken);
    /// <summary>Locks the terminal row FOR SHARE; false when it is not an active terminal of the business's outlet.</summary>
    Task<bool> LockActiveTerminalAsync(long businessId, long outletId, long terminalId, CancellationToken cancellationToken);

    Task<Exchange?> FindByIdempotencyKeyAsync(long businessId, string idempotencyKey, CancellationToken cancellationToken);
    Task<ExchangeReversal?> FindReversalByIdempotencyKeyAsync(long businessId, string idempotencyKey, CancellationToken cancellationToken);
    Task<Exchange?> LoadExchangeForUpdateAsync(long businessId, long exchangeId, CancellationToken cancellationToken);

    /// <summary>Unlocked, untracked load of an exchange with its lines (reversal eligibility).</summary>
    Task<Exchange?> LoadExchangeAsync(long businessId, long exchangeId, CancellationToken cancellationToken);

    /// <summary>
    /// Locks the replacement lines FOR UPDATE in one ascending statement and returns their committed returned quantities,
    /// read after the lock (never from the change tracker).
    /// </summary>
    Task<IReadOnlyDictionary<long, decimal>> LockReplacementLineQuantitiesAsync(IReadOnlyCollection<long> lineIds, CancellationToken cancellationToken);

    /// <summary>The stock ledger rows the exchange wrote at its receiving outlet, in id order.</summary>
    Task<IReadOnlyList<ExchangeStockMovement>> LoadStockMovementsAsync(long exchangeId, long outletId, CancellationToken cancellationToken);

    /// <summary>The completion's salesperson fact (no reversal id).</summary>
    Task<ExchangeSalespersonAdjustment?> FindSalespersonAdjustmentAsync(long exchangeId, CancellationToken cancellationToken);

    /// <summary>Takes the next exchanges.id from its identity sequence, so references can be written before the single insert.</summary>
    Task<long> ReserveExchangeIdAsync(CancellationToken cancellationToken);
    Task<long> ReserveReversalIdAsync(CancellationToken cancellationToken);
    Task AddExchangeAsync(Exchange exchange, CancellationToken cancellationToken);
    Task AddLineageLinksAsync(IReadOnlyCollection<ExchangeLineageLink> links, CancellationToken cancellationToken);
    Task AddSalespersonAdjustmentAsync(ExchangeSalespersonAdjustment adjustment, CancellationToken cancellationToken);
    Task AddReceiptSnapshotAsync(ExchangeReceiptSnapshot snapshot, CancellationToken cancellationToken);
    Task AddReversalAsync(ExchangeReversal reversal, CancellationToken cancellationToken);
    Task AddSettlementsAsync(IReadOnlyCollection<ExchangeSettlement> settlements, CancellationToken cancellationToken);
    Task AddPrintHistoryAsync(ExchangeReceiptPrintHistory entry, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
    bool IsUniqueViolation(Exception exception);
    bool IsConcurrencyFailure(Exception exception);
}

public sealed record ExchangeJournalPosting(
    long BusinessId,
    long ActorUserId,
    long OutletId,
    long TerminalId,
    long CashSessionId,
    DateTime BusinessDate,
    DateTime TransactionAtUtc,
    string ReferenceType,
    string ReferenceId,
    string ReferenceNumber,
    string Description,
    string SourceKey,
    IReadOnlyList<ExchangeJournalLine> Lines);

/// <summary>Resolves Exchange journal slots with the business's existing COA mappings and posts through the journal engine (C-04, C-06).</summary>
public interface IExchangeJournalPoster
{
    /// <summary>Fails with a typed Exchange error when a required slot or settlement account is not mapped; no fallback.</summary>
    Task EnsureAccountsAsync(long outletId, IReadOnlyCollection<string> slots, IReadOnlyCollection<string> settlementMethods, DateTime businessDate, CancellationToken cancellationToken);

    /// <summary>Creates and posts the journal inside the caller's transaction and returns its id.</summary>
    Task<long> PostAsync(ExchangeJournalPosting posting, CancellationToken cancellationToken);
}

public interface IExchangeAuditWriter
{
    Task RecordAsync(string action, string summary, long exchangeId, IReadOnlyDictionary<string, object?> metadata, CancellationToken cancellationToken);
}

public interface IExchangeEventWriter
{
    Task AppendAsync(long businessId, string messageType, long exchangeId, string discriminator, object payload, DateTime occurredAtUtc, CancellationToken cancellationToken);
}
