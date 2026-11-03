using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using RetailPOS.Application.Sales.FinancialPostings;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Sales;

public sealed class SaleFinancialPostingStore(RetailPOSDbContext dbContext) : ISaleFinancialPostingStore
{
    public async Task<ISaleFinancialPostingTransaction> BeginTransactionAsync(CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsRelational() || dbContext.Database.CurrentTransaction is not null)
            return NoopTransaction.Instance;
        return new EfTransaction(
            await dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken));
    }

    public async Task<SaleFinancialPosting?> LockAsync(long postingId, bool skipLocked, CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsNpgsql())
            return await dbContext.SaleFinancialPostings.FirstOrDefaultAsync(row => row.Id == postingId, cancellationToken);

        if (dbContext.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Locking a financial posting requires an open database transaction.");

        var query = skipLocked
            ? dbContext.SaleFinancialPostings.FromSqlInterpolated(
                $"SELECT * FROM sale_financial_postings WHERE id = {postingId} FOR UPDATE SKIP LOCKED")
            : dbContext.SaleFinancialPostings.FromSqlInterpolated(
                $"SELECT * FROM sale_financial_postings WHERE id = {postingId} FOR UPDATE");
        var row = await query.AsTracking().FirstOrDefaultAsync(cancellationToken);
        // Reload so a copy tracked before the lock cannot hide a transition another worker committed.
        if (row is not null)
            await dbContext.Entry(row).ReloadAsync(cancellationToken);
        return row;
    }

    public Task<SaleFinancialPosting?> FindAsync(long postingId, CancellationToken cancellationToken) =>
        dbContext.SaleFinancialPostings.AsNoTracking().FirstOrDefaultAsync(row => row.Id == postingId, cancellationToken);

    public Task<string?> FindSalePostingStatusAsync(long saleId, CancellationToken cancellationToken) =>
        dbContext.SaleFinancialPostings.AsNoTracking()
            .Where(row => row.SaleId == saleId && row.Kind == SaleFinancialPostingKinds.Sale)
            .Select(row => row.Status)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<long, long?>> GetPaymentAccountsAsync(
        long saleId,
        CancellationToken cancellationToken) =>
        await dbContext.SalePayments.AsNoTracking()
            .Where(payment => payment.SaleId == saleId)
            .ToDictionaryAsync(payment => payment.Id, payment => payment.PaymentAccountId, cancellationToken);

    public async Task SnapshotPaymentAccountsAsync(
        long saleId,
        IReadOnlyDictionary<long, long> accounts,
        CancellationToken cancellationToken)
    {
        if (accounts.Count == 0)
            return;
        // A List, not an array: C# 14 binds array.Contains to a ReadOnlySpan overload EF cannot evaluate.
        var ids = accounts.Keys.ToList();
        var payments = await dbContext.SalePayments
            .Where(payment => payment.SaleId == saleId && ids.Contains(payment.Id) && payment.PaymentAccountId == null)
            .ToListAsync(cancellationToken);
        foreach (var payment in payments)
            payment.PaymentAccountId = accounts[payment.Id];
    }

    public async Task<IReadOnlyList<SaleFinancialPostingDue>> ListDueAsync(
        DateTime nowUtc,
        int take,
        CancellationToken cancellationToken) =>
        await dbContext.SaleFinancialPostings.AsNoTracking()
            .Where(row => row.Status != SaleFinancialPostingStatuses.Posted
                && (row.NextAttemptAtUtc == null || row.NextAttemptAtUtc <= nowUtc))
            .OrderBy(row => row.NextAttemptAtUtc).ThenBy(row => row.Id)
            .Take(take)
            .Select(row => new SaleFinancialPostingDue(row.Id, row.BusinessId, row.OutletId, row.ActorUserId))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SaleFinancialPosting>> ListAsync(
        long businessId,
        string? status,
        int take,
        CancellationToken cancellationToken)
    {
        var query = dbContext.SaleFinancialPostings.AsNoTracking().Where(row => row.BusinessId == businessId);
        if (status is not null)
            query = query.Where(row => row.Status == status);
        return await query.OrderByDescending(row => row.Id).Take(take).ToListAsync(cancellationToken);
    }

    public async Task<SaleFinancialPostingSummary> SummarizeAsync(long businessId, CancellationToken cancellationToken)
    {
        var rows = dbContext.SaleFinancialPostings.AsNoTracking().Where(row => row.BusinessId == businessId);
        var counts = await rows.GroupBy(row => row.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);
        int Count(string status) => counts.FirstOrDefault(entry => entry.Status == status)?.Count ?? 0;
        var awaiting = await rows.CountAsync(row => row.Status != SaleFinancialPostingStatuses.Posted
            && row.LastErrorCode == SaleFinancialPostingErrorCodes.AwaitingSalePosting, cancellationToken);
        var oldest = await rows.Where(row => row.Status != SaleFinancialPostingStatuses.Posted)
            .OrderBy(row => row.CreatedAtUtc)
            .Select(row => (DateTime?)row.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        return new SaleFinancialPostingSummary(
            Count(SaleFinancialPostingStatuses.Pending),
            Count(SaleFinancialPostingStatuses.Failed),
            Count(SaleFinancialPostingStatuses.Posted),
            awaiting,
            UtcTime.Normalize(oldest));
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);

    public void DiscardPendingChanges() => dbContext.ChangeTracker.Clear();

    private sealed class EfTransaction(IDbContextTransaction transaction) : ISaleFinancialPostingTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken) => transaction.CommitAsync(cancellationToken);

        public Task RollbackAsync(CancellationToken cancellationToken) => transaction.RollbackAsync(cancellationToken);

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }

    private sealed class NoopTransaction : ISaleFinancialPostingTransaction
    {
        public static NoopTransaction Instance { get; } = new();

        public Task CommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task RollbackAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
