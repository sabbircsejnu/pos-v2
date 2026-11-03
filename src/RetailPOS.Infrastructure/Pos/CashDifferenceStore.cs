using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Pos.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Pos;

public sealed class CashDifferenceStore(RetailPOSDbContext dbContext) : ICashDifferenceStore
{
    public Task<CashDifferenceApproval?> LockApprovalAsync(long approvalId, CancellationToken cancellationToken) =>
        LockApprovalAsync(approvalId, skipLocked: false, cancellationToken);

    public async Task<CashDifferenceApproval?> LockApprovalAsync(
        long approvalId,
        bool skipLocked,
        CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsNpgsql())
            return await dbContext.CashDifferenceApprovals.FirstOrDefaultAsync(row => row.Id == approvalId, cancellationToken);

        if (dbContext.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Locking a cash difference requires an open database transaction.");

        // Tracked query: reload so a previously tracked copy cannot hide a decision committed by another request.
        var query = skipLocked
            ? dbContext.CashDifferenceApprovals.FromSqlInterpolated(
                $"SELECT * FROM cash_difference_approvals WHERE id = {approvalId} FOR UPDATE SKIP LOCKED")
            : dbContext.CashDifferenceApprovals.FromSqlInterpolated(
                $"SELECT * FROM cash_difference_approvals WHERE id = {approvalId} FOR UPDATE");
        var row = await query.AsTracking().FirstOrDefaultAsync(cancellationToken);
        if (row is not null)
            await dbContext.Entry(row).ReloadAsync(cancellationToken);
        return row;
    }

    public async Task<IReadOnlyList<CashDifferencePostingDue>> ListDueForPostingAcrossBusinessesAsync(
        DateTime nowUtc,
        int take,
        CancellationToken cancellationToken) =>
        await dbContext.CashDifferenceApprovals.AsNoTracking()
            .Where(row => row.Status == CashDifferenceStatuses.Approved
                && row.ReviewedByUserId != null
                && (row.PostingStatus == CashDifferencePostingStatuses.Pending
                    || row.PostingStatus == CashDifferencePostingStatuses.Failed)
                && (row.NextPostingAttemptAtUtc == null || row.NextPostingAttemptAtUtc <= nowUtc))
            .OrderBy(row => row.NextPostingAttemptAtUtc).ThenBy(row => row.Id)
            .Take(take)
            .Select(row => new CashDifferencePostingDue(row.Id, row.BusinessId, row.OutletId, row.ReviewedByUserId!.Value))
            .ToListAsync(cancellationToken);

    public Task<CashDifferenceApproval?> FindAsync(long approvalId, CancellationToken cancellationToken) =>
        dbContext.CashDifferenceApprovals.AsNoTracking().FirstOrDefaultAsync(row => row.Id == approvalId, cancellationToken);

    public async Task<IReadOnlyList<long>> ListDueForPostingAsync(
        long businessId,
        long? outletId,
        DateTime nowUtc,
        int take,
        CancellationToken cancellationToken)
    {
        var query = dbContext.CashDifferenceApprovals.AsNoTracking()
            .Where(row => row.BusinessId == businessId
                && row.Status == CashDifferenceStatuses.Approved
                && (row.PostingStatus == CashDifferencePostingStatuses.Pending
                    || row.PostingStatus == CashDifferencePostingStatuses.Failed)
                && (row.NextPostingAttemptAtUtc == null || row.NextPostingAttemptAtUtc <= nowUtc));
        if (outletId.HasValue)
            query = query.Where(row => row.OutletId == outletId.Value);
        return await query.OrderBy(row => row.NextPostingAttemptAtUtc).ThenBy(row => row.Id)
            .Select(row => row.Id).Take(take).ToListAsync(cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);

    public void DiscardPendingChanges() => dbContext.ChangeTracker.Clear();
}
