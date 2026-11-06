using System.Data;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.OwnerDashboards;
using RetailPOS.Application.Sales.FinancialPostings;
using RetailPOS.Application.Settings;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.OwnerDashboards;

/// <summary>One recorded void or refund. Untrusted records move no money: nothing is guessed for them.</summary>
internal sealed record ReversalFact(
    long SaleId,
    long OutletId,
    DateOnly AccountingDate,
    bool Trusted,
    SaleReversalJournalSnapshot Snapshot);

/// <summary>The one definition of "counted sale" shared by every dashboard metric (SF-024).</summary>
internal static class OwnerDashboardSales
{
    public static DateTime ToColumnDate(DateOnly date) => date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    /// <summary>
    /// Sales attributed to the inclusive business-date range. A voided sale stays in the period it was sold in and is
    /// offset by its reversal on the reversal's own date, as in the journals (P3.2-D02). A legacy void with no reversal
    /// record has no such offset, so it is left out. Held sales live in their own table and never appear.
    /// </summary>
    public static IQueryable<Sale> InPeriod(
        RetailPOSDbContext dbContext,
        long businessId,
        List<long> outletIds,
        DateOnly from,
        DateOnly to,
        string timeZoneId)
    {
        var (startUtc, endUtc) = CompanyDates.DayWindow(from, to, timeZoneId);
        var fromDate = ToColumnDate(from);
        var toDate = ToColumnDate(to);
        return dbContext.Sales
            .AsNoTracking()
            .Where(sale =>
                outletIds.Contains(sale.OutletId)
                && (sale.BusinessId == null || sale.BusinessId == businessId)
                && ((sale.BusinessDate != null && sale.BusinessDate >= fromDate && sale.BusinessDate <= toDate)
                    || (sale.BusinessDate == null && sale.SaleDate >= startUtc && sale.SaleDate < endUtc))
                && (sale.Status.ToLower() == "completed"
                    || sale.Status.ToLower() == "refunded"
                    || (sale.Status.ToLower() == "voided"
                        && dbContext.SaleFinancialPostings.Any(posting =>
                            posting.SaleId == sale.Id
                            && posting.Kind == SaleFinancialPostingKinds.Reversal))));
    }

    /// <summary>Reversals dated in the range, read from their immutable snapshots.</summary>
    public static async Task<IReadOnlyList<ReversalFact>> ReversalsAsync(
        RetailPOSDbContext dbContext,
        long businessId,
        List<long> outletIds,
        DateOnly from,
        DateOnly to,
        string timeZoneId,
        CancellationToken cancellationToken)
    {
        // The accounting date lives in the snapshot; one day of padding on the commit time finds every candidate.
        var (startUtc, endUtc) = CompanyDates.DayWindow(from.AddDays(-1), to.AddDays(1), timeZoneId);
        var postings = await dbContext.SaleFinancialPostings
            .AsNoTracking()
            .Where(posting =>
                posting.BusinessId == businessId
                && posting.Kind == SaleFinancialPostingKinds.Reversal
                && outletIds.Contains(posting.OutletId)
                && posting.CreatedAtUtc >= startUtc
                && posting.CreatedAtUtc < endUtc)
            .ToListAsync(cancellationToken);

        return postings
            .Select(posting =>
            {
                var snapshot = SaleFinancialIntentFactory.ReadReversalSnapshot(posting);
                var trusted = snapshot.Version >= SaleFinancialIntentFactory.SnapshotVersion
                    && snapshot.MissingEvidence is null
                    && snapshot.AccountingDate is not null;
                var date = snapshot.AccountingDate
                    ?? DateOnly.FromDateTime(CompanyDates.DateFor(posting.CreatedAtUtc, timeZoneId));
                return new ReversalFact(posting.SaleId, posting.OutletId, date, trusted, snapshot);
            })
            .Where(reversal => reversal.AccountingDate >= from && reversal.AccountingDate <= to)
            .ToList();
    }

    /// <summary>Runs reads in one read-only snapshot: consistent totals, no writes possible, no writers blocked.</summary>
    public static async Task<T> InReadOnlySnapshotAsync<T>(
        RetailPOSDbContext dbContext,
        Func<Task<T>> read,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead,
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", cancellationToken);
        var result = await read();
        await transaction.CommitAsync(cancellationToken);
        return result;
    }
}
