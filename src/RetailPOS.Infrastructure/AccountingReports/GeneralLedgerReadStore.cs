using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.AccountingReports.Abstractions;
using RetailPOS.Application.AccountingReports.ReadModels;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.AccountingReports;

public sealed class GeneralLedgerReadStore(RetailPOSDbContext dbContext)
    : IGeneralLedgerReadStore
{
    public async Task<GeneralLedgerReportReadModel> GetGeneralLedgerAsync(
        GeneralLedgerReportFilter filter,
        long businessId,
        CancellationToken cancellationToken)
    {
        var journals = await BuildQuery(filter, businessId)
            .Include(journal => journal.Lines)
                .ThenInclude(line => line.Account)
            .OrderBy(journal => journal.PostingDate)
            .ThenBy(journal => journal.Id)
            .ToListAsync(cancellationToken);

        var rows = journals
            .SelectMany(journal => journal.Lines)
            .GroupBy(line => new { line.AccountId, line.Account.AccountCode, line.Account.AccountName })
            .Select(group => new GeneralLedgerRowReadModel(
                group.Key.AccountCode,
                group.Key.AccountName,
                string.Empty,
                group.Sum(line => line.DebitAmount),
                group.Sum(line => line.CreditAmount),
                group.Sum(line => line.DebitAmount) - group.Sum(line => line.CreditAmount)))
            .OrderBy(row => row.AccountCode)
            .ToList();

        return new GeneralLedgerReportReadModel(rows);
    }

    public async Task<TrialBalanceReportReadModel> GetTrialBalanceAsync(
        GeneralLedgerReportFilter filter,
        long businessId,
        CancellationToken cancellationToken)
    {
        var journals = await BuildQuery(filter, businessId)
            .Include(journal => journal.Lines)
                .ThenInclude(line => line.Account)
            .ToListAsync(cancellationToken);

        var rows = journals
            .SelectMany(journal => journal.Lines)
            .GroupBy(line => new { line.AccountId, line.Account.AccountCode, line.Account.AccountName })
            .Select(group => new TrialBalanceRowReadModel(
                group.Key.AccountCode,
                group.Key.AccountName,
                group.Sum(line => line.DebitAmount),
                group.Sum(line => line.CreditAmount)))
            .OrderBy(row => row.AccountCode)
            .ToList();

        return new TrialBalanceReportReadModel(
            rows,
            rows.Sum(row => row.DebitAmount),
            rows.Sum(row => row.CreditAmount));
    }

    public async Task<ProfitLossReportReadModel> GetProfitLossAsync(
        GeneralLedgerReportFilter filter,
        long businessId,
        CancellationToken cancellationToken)
    {
        var journals = await BuildQuery(filter, businessId)
            .Where(journal => journal.ReferenceType != AccountingReferenceTypes.FinancialYearClose)
            .Include(journal => journal.Lines)
                .ThenInclude(line => line.Account)
            .ToListAsync(cancellationToken);
        var lines = journals.SelectMany(journal => journal.Lines).ToList();
        var revenue = lines
            .Where(line => string.Equals(
                line.Account.AccountType,
                ChartAccountTypes.Income,
                StringComparison.OrdinalIgnoreCase))
            .Sum(line => line.CreditAmount - line.DebitAmount);
        var costOfGoodsSold = lines
            .Where(line => string.Equals(
                line.Account.AccountType,
                ChartAccountTypes.CostOfGoodsSold,
                StringComparison.OrdinalIgnoreCase))
            .Sum(line => line.DebitAmount - line.CreditAmount);
        var expenses = lines
            .Where(line => string.Equals(
                line.Account.AccountType,
                ChartAccountTypes.Expense,
                StringComparison.OrdinalIgnoreCase))
            .Sum(line => line.DebitAmount - line.CreditAmount);

        return new ProfitLossReportReadModel(
            revenue,
            costOfGoodsSold,
            expenses,
            revenue - costOfGoodsSold - expenses);
    }

    public async Task<ProfitLossReportReadModel> GetHistoricalProfitLossAsync(
        DateTime dateFrom,
        DateTime dateTo,
        long? locationId,
        string? locationType,
        long businessId,
        CancellationToken cancellationToken)
    {
        var upperBound = dateTo.Date.AddDays(1).AddTicks(-1);
        var journals = await dbContext.JournalEntries
            .AsNoTracking()
            .Where(journal => journal.BusinessId == businessId
                && (journal.Status == JournalEntryStatuses.Posted
                    || (journal.Status == JournalEntryStatuses.Reversed
                        && journal.ReversedByJournalEntryId.HasValue))
                && journal.PostingDate <= upperBound
                && journal.PostingDate >= dateFrom.Date
                && journal.ReferenceType != AccountingReferenceTypes.FinancialYearClose)
            .Include(journal => journal.Lines)
                .ThenInclude(line => line.Account)
            .ToListAsync(cancellationToken);
        var lines = journals
            .SelectMany(journal => journal.Lines
                .Where(line => MatchesLocation(journal, line, locationId, locationType)))
            .ToList();
        var revenue = lines
            .Where(line => line.Account.AccountType == ChartAccountTypes.Income)
            .Sum(line => line.CreditAmount - line.DebitAmount);
        var costOfGoodsSold = lines
            .Where(line => line.Account.AccountType == ChartAccountTypes.CostOfGoodsSold)
            .Sum(line => line.DebitAmount - line.CreditAmount);
        var expenses = lines
            .Where(line => line.Account.AccountType == ChartAccountTypes.Expense)
            .Sum(line => line.DebitAmount - line.CreditAmount);
        return new ProfitLossReportReadModel(
            revenue,
            costOfGoodsSold,
            expenses,
            revenue - costOfGoodsSold - expenses);
    }

    private IQueryable<JournalEntry> BuildQuery(GeneralLedgerReportFilter filter, long businessId)
    {
        var query = dbContext.JournalEntries
            .AsNoTracking()
            .Where(journal => journal.BusinessId == businessId
                && journal.Status == JournalEntryStatuses.Posted);

        if (filter.DateFrom.HasValue)
            query = query.Where(journal => journal.PostingDate >= filter.DateFrom.Value.Date);

        if (filter.DateTo.HasValue)
        {
            var inclusiveEnd = filter.DateTo.Value.Date.AddDays(1).AddTicks(-1);
            query = query.Where(journal => journal.PostingDate <= inclusiveEnd);
        }

        if (filter.OutletId.HasValue)
            query = query.Where(journal => journal.OutletId == filter.OutletId.Value);

        if (filter.FinancialYearId.HasValue)
            query = query.Where(journal => journal.FinancialYearId == filter.FinancialYearId.Value);

        if (filter.AccountingPeriodId.HasValue)
            query = query.Where(journal => journal.AccountingPeriodId == filter.AccountingPeriodId.Value);

        return query;
    }

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
}
