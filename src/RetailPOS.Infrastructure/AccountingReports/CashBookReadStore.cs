using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.AccountingReports.Abstractions;
using RetailPOS.Application.AccountingReports.ReadModels;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.AccountingReports;

public sealed class CashBookReadStore(RetailPOSDbContext dbContext) : ICashBookReadStore
{
    public async Task<CashBookReadModel> GetAsync(
        CashBookFilter filter,
        long businessId,
        IReadOnlyList<CashBookAccountResolution> resolutions,
        CancellationToken cancellationToken)
    {
        var dateFrom = filter.DateFrom!.Value;
        var dateTo = filter.DateTo!.Value;
        var accountIds = resolutions.Select(resolution => resolution.AccountId).Distinct().ToList();
        var accounts = await dbContext.ChartAccounts
            .AsNoTracking()
            .Where(account => accountIds.Contains(account.Id))
            .ToDictionaryAsync(account => account.Id, cancellationToken);
        var journals = await dbContext.JournalEntries
            .AsNoTracking()
            .Include(journal => journal.Lines)
            .ThenInclude(line => line.Account)
            .Where(journal => journal.BusinessId == businessId
                && (journal.Status == JournalEntryStatuses.Posted
                    || (journal.Status == JournalEntryStatuses.Reversed
                        && journal.ReversedByJournalEntryId.HasValue))
                && journal.PostingDate <= dateTo)
            .OrderBy(journal => journal.PostingDate)
            .ThenBy(journal => journal.JournalNumber)
            .ThenBy(journal => journal.Id)
            .ToListAsync(cancellationToken);

        var accountReports = new List<CashBookAccountReadModel>();
        foreach (var resolution in resolutions
            .GroupBy(resolution => resolution.AccountId)
            .Select(group => group.First()))
        {
            var account = accounts[resolution.AccountId];
            var relevant = journals
                .SelectMany(journal => journal.Lines
                    .Where(line => line.AccountId == resolution.AccountId)
                    .Select(line => new { Journal = journal, Line = line }))
                .Where(entry => MatchesFilter(entry.Journal, entry.Line, filter))
                .OrderBy(entry => entry.Journal.PostingDate)
                .ThenBy(entry => entry.Journal.JournalNumber)
                .ThenBy(entry => entry.Journal.Id)
                .ThenBy(entry => entry.Line.Id)
                .ToList();
            var opening = relevant
                .Where(entry => entry.Journal.PostingDate < dateFrom)
                .Sum(entry => entry.Line.DebitAmount - entry.Line.CreditAmount);
            var running = opening;
            var rows = new List<CashBookRowReadModel>();
            foreach (var entry in relevant.Where(entry => entry.Journal.PostingDate >= dateFrom))
            {
                running += entry.Line.DebitAmount - entry.Line.CreditAmount;
                rows.Add(new CashBookRowReadModel(
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
                    string.Join(", ", entry.Journal.Lines
                        .Where(line => !ReferenceEquals(line, entry.Line))
                        .Select(line => line.Account.AccountName)
                        .Where(name => !string.IsNullOrWhiteSpace(name))
                        .Distinct()),
                    entry.Journal.Lines
                        .Where(line => !ReferenceEquals(line, entry.Line))
                        .Select(line => line.AccountId)
                        .Distinct()
                        .ToList(),
                    entry.Journal.ReversalOfJournalEntryId,
                    entry.Journal.ReversedByJournalEntryId));
            }

            var inflows = rows.Sum(row => row.Inflow);
            var outflows = rows.Sum(row => row.Outflow);
            var closing = opening + inflows - outflows;
            var glBalance = relevant.Sum(entry =>
                entry.Line.DebitAmount - entry.Line.CreditAmount);
            accountReports.Add(new CashBookAccountReadModel(
                resolution.Slot,
                account.Id,
                account.AccountCode,
                account.AccountName,
                opening,
                inflows,
                outflows,
                closing,
                glBalance,
                closing - glBalance,
                rows));
        }

        return new CashBookReadModel(dateFrom, dateTo.Date, accountReports);
    }

    private static bool MatchesFilter(
        JournalEntry journal,
        JournalEntryLine line,
        CashBookFilter filter)
    {
        var locationType = filter.LocationType?.Trim().ToLowerInvariant();
        if (filter.LocationId.HasValue)
        {
            if (locationType == "outlet"
                && (line.OutletId ?? journal.OutletId) != filter.LocationId)
                return false;
            if (locationType == "warehouse"
                && (line.WarehouseId ?? journal.WarehouseId) != filter.LocationId)
                return false;
        }

        if (!string.IsNullOrWhiteSpace(filter.JournalType)
            && !string.Equals(
                journal.JournalType,
                filter.JournalType,
                StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.IsNullOrWhiteSpace(filter.ReferenceType)
            && !string.Equals(
                journal.ReferenceType,
                filter.ReferenceType,
                StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.IsNullOrWhiteSpace(filter.Reference)
            && !(journal.ReferenceId.Contains(
                    filter.Reference,
                    StringComparison.OrdinalIgnoreCase)
                || (journal.ReferenceNumber?.Contains(
                    filter.Reference,
                    StringComparison.OrdinalIgnoreCase) ?? false)))
            return false;
        return true;
    }
}
