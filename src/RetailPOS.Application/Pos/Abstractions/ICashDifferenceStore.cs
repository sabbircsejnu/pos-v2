using RetailPOS.Core.Entities;

namespace RetailPOS.Application.Pos.Abstractions;

public sealed record CashDifferencePostingDue(long ApprovalId, long BusinessId, long OutletId, long ReviewerUserId);

public interface ICashDifferenceStore
{
    /// <summary>Reads the approval and takes <c>FOR UPDATE</c> inside the current transaction; the instance is tracked.</summary>
    Task<CashDifferenceApproval?> LockApprovalAsync(long approvalId, CancellationToken cancellationToken);

    /// <summary>
    /// As <see cref="LockApprovalAsync(long, CancellationToken)"/>; with <paramref name="skipLocked"/> a row another
    /// worker is posting returns null instead of waiting (<c>FOR UPDATE SKIP LOCKED</c>).
    /// </summary>
    Task<CashDifferenceApproval?> LockApprovalAsync(long approvalId, bool skipLocked, CancellationToken cancellationToken);

    /// <summary>Approved differences due for posting in every Business, for the background worker only.</summary>
    Task<IReadOnlyList<CashDifferencePostingDue>> ListDueForPostingAcrossBusinessesAsync(
        DateTime nowUtc,
        int take,
        CancellationToken cancellationToken);

    Task<CashDifferenceApproval?> FindAsync(long approvalId, CancellationToken cancellationToken);

    Task<IReadOnlyList<long>> ListDueForPostingAsync(
        long businessId,
        long? outletId,
        DateTime nowUtc,
        int take,
        CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>Drops unsaved tracked changes after a rolled-back unit of work.</summary>
    void DiscardPendingChanges();
}
