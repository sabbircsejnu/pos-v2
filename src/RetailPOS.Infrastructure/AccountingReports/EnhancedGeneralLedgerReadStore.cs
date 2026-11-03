using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.AccountingReports.Abstractions;
using RetailPOS.Application.AccountingReports.ReadModels;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.AccountingReports;

public sealed class EnhancedGeneralLedgerReadStore(RetailPOSDbContext dbContext)
    : IEnhancedGeneralLedgerReadStore
{
    public async Task<EnhancedGeneralLedgerReadModel> GetAsync(
        EnhancedGeneralLedgerFilter filter,
        long businessId,
        CancellationToken cancellationToken)
    {
        ValidateLocationType(filter.LocationType);
        var account = await dbContext.ChartAccounts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.BusinessId == businessId
                    && candidate.Id == filter.AccountId,
                cancellationToken)
            ?? throw new KeyNotFoundException("Chart account was not found in this business.");
        var accountHierarchy = await dbContext.ChartAccounts
            .AsNoTracking()
            .Where(candidate => candidate.BusinessId == businessId)
            .Select(candidate => new AccountHierarchyRow(candidate.Id, candidate.ParentAccountId))
            .ToListAsync(cancellationToken);
        var accountIds = GetDescendantAccountIds(account.Id, accountHierarchy);
        var dateFrom = (filter.DateFrom ?? DateTime.UtcNow.Date).Date;
        var dateTo = (filter.DateTo ?? DateTime.UtcNow).Date.AddDays(1).AddTicks(-1);
        if (dateTo < dateFrom)
            throw new InvalidOperationException("DateTo must not be earlier than DateFrom.");

        var journals = await HistoricallyPostedJournalsQuery(businessId, dateTo.Date)
            .Include(journal => journal.Lines)
            .Where(journal => string.IsNullOrWhiteSpace(filter.JournalType)
                || journal.JournalType == filter.JournalType)
            .Where(journal => string.IsNullOrWhiteSpace(filter.ReferenceType)
                || journal.ReferenceType == filter.ReferenceType)
            .Where(journal => string.IsNullOrWhiteSpace(filter.Reference)
                || journal.ReferenceId.Contains(filter.Reference)
                || (journal.ReferenceNumber != null
                    && journal.ReferenceNumber.Contains(filter.Reference)))
            .OrderBy(journal => journal.PostingDate)
            .ThenBy(journal => journal.JournalNumber)
            .ThenBy(journal => journal.Id)
            .ToListAsync(cancellationToken);
        var entries = journals
            .SelectMany(journal => journal.Lines
                .Where(line => accountIds.Contains(line.AccountId)
                    && MatchesLocation(journal, line, filter.LocationId, filter.LocationType))
                .Select(line => new { Journal = journal, Line = line }))
            .OrderBy(entry => entry.Journal.PostingDate)
            .ThenBy(entry => entry.Journal.JournalNumber)
            .ThenBy(entry => entry.Journal.Id)
            .ThenBy(entry => entry.Line.Id)
            .ToList();
        var opening = CalculateNormalBalance(
            account,
            entries.Where(entry => entry.Journal.PostingDate < dateFrom)
                .Sum(entry => entry.Line.DebitAmount),
            entries.Where(entry => entry.Journal.PostingDate < dateFrom)
                .Sum(entry => entry.Line.CreditAmount));
        var running = opening;
        var rows = entries
            .Where(entry => entry.Journal.PostingDate >= dateFrom)
            .Select(entry =>
            {
                running += CalculateNormalBalance(
                    account,
                    entry.Line.DebitAmount,
                    entry.Line.CreditAmount);
                return new EnhancedGeneralLedgerRowReadModel(
                    entry.Journal.Id,
                    entry.Line.Id,
                    entry.Journal.PostingDate,
                    entry.Journal.BusinessDate,
                    entry.Journal.JournalNumber,
                    entry.Journal.JournalType,
                    entry.Journal.ReferenceType,
                    entry.Journal.ReferenceId,
                    entry.Journal.ReferenceNumber,
                    entry.Line.Description ?? entry.Journal.Description,
                    entry.Line.DebitAmount,
                    entry.Line.CreditAmount,
                    running,
                    entry.Line.OutletId ?? entry.Journal.OutletId,
                    entry.Line.WarehouseId ?? entry.Journal.WarehouseId,
                    entry.Line.CustomerId,
                    entry.Line.SupplierId,
                    entry.Line.PaymentMethodId,
                    entry.Line.CostCenterId,
                    entry.Journal.ReversalOfJournalEntryId,
                    entry.Journal.ReversedByJournalEntryId);
            })
            .ToList();
        return new EnhancedGeneralLedgerReadModel(
            account.Id,
            account.AccountCode,
            account.AccountName,
            account.NormalBalanceType,
            opening,
            rows.Sum(row => row.DebitAmount),
            rows.Sum(row => row.CreditAmount),
            running,
            rows);
    }

    private IQueryable<JournalEntry> HistoricallyPostedJournalsQuery(
        long businessId,
        DateTime asOfDate) =>
        dbContext.JournalEntries
            .AsNoTracking()
            .Where(journal => journal.BusinessId == businessId
                && (journal.Status == JournalEntryStatuses.Posted
                    || (journal.Status == JournalEntryStatuses.Reversed
                        && journal.ReversedByJournalEntryId.HasValue))
                && journal.PostingDate <= asOfDate.Date.AddDays(1).AddTicks(-1));

    private static HashSet<long> GetDescendantAccountIds(
        long accountId,
        IEnumerable<AccountHierarchyRow> hierarchy)
    {
        var accountIds = new HashSet<long> { accountId };
        var pending = new Queue<long>();
        pending.Enqueue(accountId);
        var rows = hierarchy.ToList();
        while (pending.TryDequeue(out var parentId))
        {
            foreach (var childId in rows
                .Where(row => row.ParentAccountId == parentId)
                .Select(row => row.Id))
            {
                if (accountIds.Add(childId))
                    pending.Enqueue(childId);
            }
        }

        return accountIds;
    }

    private sealed record AccountHierarchyRow(long Id, long? ParentAccountId);

    private static bool MatchesLocation(
        JournalEntry journal,
        JournalEntryLine line,
        long? locationId,
        string? locationType)
    {
        if (!locationId.HasValue)
            return true;
        return string.Equals(locationType, "warehouse", StringComparison.OrdinalIgnoreCase)
            ? (line.WarehouseId ?? journal.WarehouseId) == locationId
            : (line.OutletId ?? journal.OutletId) == locationId;
    }

    private static void ValidateLocationType(string? locationType)
    {
        if (!string.IsNullOrWhiteSpace(locationType)
            && !string.Equals(locationType, "outlet", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(locationType, "warehouse", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("LocationType must be either outlet or warehouse.");
    }

    private static decimal CalculateNormalBalance(
        ChartAccount account,
        decimal debit,
        decimal credit) =>
        string.Equals(
            account.NormalBalanceType,
            NormalBalanceTypes.Credit,
            StringComparison.OrdinalIgnoreCase)
            ? credit - debit
            : debit - credit;
}
