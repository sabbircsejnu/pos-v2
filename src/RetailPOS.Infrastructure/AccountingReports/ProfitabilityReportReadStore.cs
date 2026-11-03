using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.AccountingReports.Abstractions;
using RetailPOS.Application.AccountingReports.ReadModels;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.AccountingReports;

public sealed class ProfitabilityReportReadStore(RetailPOSDbContext dbContext)
    : IProfitabilityReportReadStore
{
    public async Task<BusinessProfitabilityReadModel> GetBusinessAsync(
        ProfitabilityFilter filter,
        long businessId,
        CancellationToken cancellationToken)
    {
        var dateFrom = filter.DateFrom!.Value.Date;
        var dateTo = filter.DateTo!.Value.Date;
        var pnl = await GetProfitLossAsync(
            businessId,
            dateFrom,
            dateTo,
            filter.LocationId,
            filter.LocationType,
            cancellationToken);
        return new BusinessProfitabilityReadModel(
            dateFrom,
            dateTo,
            pnl.Revenue,
            pnl.CostOfGoodsSold,
            pnl.Expenses,
            pnl.NetProfit,
            pnl.NetProfit,
            0m);
    }

    public async Task<OutletProfitabilityReadModel> GetOutletsAsync(
        DateTime dateFrom,
        DateTime dateTo,
        long businessId,
        CancellationToken cancellationToken)
    {
        var journals = await GetProfitabilityJournalsAsync(
            businessId,
            dateFrom,
            dateTo,
            cancellationToken);
        var lines = journals
            .SelectMany(journal => journal.Lines.Select(line =>
                new
                {
                    Line = line,
                    Account = line.Account,
                    OutletId = line.OutletId ?? journal.OutletId
                }))
            .ToList();
        var outletIds = lines
            .Where(line => line.OutletId.HasValue)
            .Select(line => line.OutletId!.Value)
            .Distinct()
            .ToList();
        var outletNames = await dbContext.Outlets
            .AsNoTracking()
            .Where(outlet => outlet.BusinessId == businessId
                && outletIds.Contains(outlet.Id))
            .ToDictionaryAsync(
                outlet => outlet.Id,
                outlet => outlet.Name,
                cancellationToken);
        var rows = lines
            .Where(line => line.OutletId.HasValue)
            .GroupBy(line => line.OutletId!.Value)
            .Select(group => BuildRow(
                group.Key,
                outletNames.GetValueOrDefault(
                    group.Key,
                    $"Outlet #{group.Key}"),
                group.Select(line => (line.Account, line.Line)).ToList()))
            .OrderBy(row => row.OutletName)
            .ToList();
        var unallocated = BuildRow(
            null,
            "Unallocated",
            lines.Where(line => !line.OutletId.HasValue)
                .Select(line => (line.Account, line.Line))
                .ToList()) with
        {
            UnallocatedReason =
                "Posted accounting activity lacks authoritative outlet attribution."
        };

        var business = await GetProfitLossAsync(
            businessId,
            dateFrom,
            dateTo,
            null,
            null,
            cancellationToken);
        var attributedRevenue = rows.Sum(row => row.Revenue) + unallocated.Revenue;
        var attributedCogs = rows.Sum(row => row.CostOfGoodsSold)
            + unallocated.CostOfGoodsSold;
        var attributedExpenses = rows.Sum(row => row.Expenses)
            + unallocated.Expenses;
        var attributedNet = attributedRevenue - attributedCogs - attributedExpenses;
        return new OutletProfitabilityReadModel(
            dateFrom.Date,
            dateTo.Date,
            rows,
            unallocated,
            business.Revenue,
            business.CostOfGoodsSold,
            business.Expenses,
            business.NetProfit,
            attributedRevenue,
            attributedCogs,
            attributedExpenses,
            attributedNet,
            attributedNet - business.NetProfit);
    }

    private async Task<BusinessProfitLoss> GetProfitLossAsync(
        long businessId,
        DateTime dateFrom,
        DateTime dateTo,
        long? locationId,
        string? locationType,
        CancellationToken cancellationToken)
    {
        var journals = await GetHistoricallyPostedJournalsQuery(
                businessId,
                dateTo)
            .Include(journal => journal.Lines)
            .ThenInclude(line => line.Account)
            .Where(journal => journal.PostingDate >= dateFrom.Date
                && journal.ReferenceType != AccountingReferenceTypes.FinancialYearClose)
            .ToListAsync(cancellationToken);
        var lines = journals
            .SelectMany(journal => journal.Lines.Where(line =>
                MatchesLocation(journal, line, locationId, locationType)))
            .ToList();
        var revenue = lines
            .Where(line => line.Account.AccountType == ChartAccountTypes.Income)
            .Sum(line => line.CreditAmount - line.DebitAmount);
        var cogs = lines
            .Where(line => line.Account.AccountType == ChartAccountTypes.CostOfGoodsSold)
            .Sum(line => line.DebitAmount - line.CreditAmount);
        var expenses = lines
            .Where(line => line.Account.AccountType == ChartAccountTypes.Expense)
            .Sum(line => line.DebitAmount - line.CreditAmount);
        return new BusinessProfitLoss(revenue, cogs, expenses, revenue - cogs - expenses);
    }

    private async Task<List<JournalEntry>> GetProfitabilityJournalsAsync(
        long businessId,
        DateTime dateFrom,
        DateTime dateTo,
        CancellationToken cancellationToken) =>
        await GetHistoricallyPostedJournalsQuery(businessId, dateTo)
            .Include(journal => journal.Lines)
            .ThenInclude(line => line.Account)
            .Where(journal => journal.PostingDate >= dateFrom.Date
                && journal.ReferenceType != AccountingReferenceTypes.FinancialYearClose)
            .ToListAsync(cancellationToken);

    private IQueryable<JournalEntry> GetHistoricallyPostedJournalsQuery(
        long businessId,
        DateTime asOfDate) =>
        dbContext.JournalEntries
            .AsNoTracking()
            .Where(journal => journal.BusinessId == businessId
                && (journal.Status == JournalEntryStatuses.Posted
                    || (journal.Status == JournalEntryStatuses.Reversed
                        && journal.ReversedByJournalEntryId.HasValue))
                && journal.PostingDate <= asOfDate.Date.AddDays(1).AddTicks(-1));

    private static OutletProfitabilityRowReadModel BuildRow(
        long? outletId,
        string outletName,
        List<(ChartAccount Account, JournalEntryLine Line)> lines)
    {
        var revenue = lines
            .Where(item => item.Account.AccountType == ChartAccountTypes.Income)
            .Sum(item => item.Line.CreditAmount - item.Line.DebitAmount);
        var cogs = lines
            .Where(item => item.Account.AccountType == ChartAccountTypes.CostOfGoodsSold)
            .Sum(item => item.Line.DebitAmount - item.Line.CreditAmount);
        var expenses = lines
            .Where(item => item.Account.AccountType == ChartAccountTypes.Expense)
            .Sum(item => item.Line.DebitAmount - item.Line.CreditAmount);
        return new OutletProfitabilityRowReadModel(
            outletId,
            outletName,
            revenue,
            cogs,
            expenses,
            revenue - cogs - expenses,
            null);
    }

    private static bool MatchesLocation(
        JournalEntry journal,
        JournalEntryLine line,
        long? locationId,
        string? locationType)
    {
        if (!locationId.HasValue)
            return true;
        return string.Equals(
                locationType,
                "warehouse",
                StringComparison.OrdinalIgnoreCase)
            ? (line.WarehouseId ?? journal.WarehouseId) == locationId
            : (line.OutletId ?? journal.OutletId) == locationId;
    }

    private sealed record BusinessProfitLoss(
        decimal Revenue,
        decimal CostOfGoodsSold,
        decimal Expenses,
        decimal NetProfit);
}
