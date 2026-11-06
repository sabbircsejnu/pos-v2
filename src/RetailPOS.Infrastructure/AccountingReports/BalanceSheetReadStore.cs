using RetailPOS.Application.Settings.Abstractions;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.AccountingReports.Abstractions;
using RetailPOS.Application.AccountingReports.ReadModels;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.AccountingReports;

public sealed class BalanceSheetReadStore(
    RetailPOSDbContext dbContext,
    ICompanyTimeZoneProvider timeZones)
    : IBalanceSheetReadStore
{
    public async Task<BalanceSheetReportReadModel> GetBalanceSheetAsync(
        BalanceSheetReportFilter filter,
        long businessId,
        CancellationToken cancellationToken)
    {
        ValidateLocationType(filter.LocationType);
        var asOfDate = (filter.AsOfDate ?? await timeZones.GetTodayAsync(businessId, cancellationToken)).Date;
        var configuration = await dbContext.AccountingConfigurations
            .AsNoTracking()
            .SingleOrDefaultAsync(configuration => configuration.BusinessId == businessId, cancellationToken)
            ?? throw new InvalidOperationException(
                "Accounting configuration is required before running a Balance Sheet.");
        if (!configuration.RetainedEarningsAccountId.HasValue)
            throw new InvalidOperationException(
                "Retained Earnings must be configured before running a Balance Sheet.");

        var accounts = await dbContext.ChartAccounts
            .AsNoTracking()
            .Where(account => account.BusinessId == businessId && account.IsActive)
            .OrderBy(account => account.DisplayOrder)
            .ThenBy(account => account.AccountCode)
            .ToListAsync(cancellationToken);
        var journals = await HistoricallyPostedJournalsQuery(businessId, asOfDate)
            .Include(journal => journal.Lines)
            .ToListAsync(cancellationToken);
        var matchingLines = journals
            .SelectMany(journal => journal.Lines.Where(line =>
                MatchesLocation(journal, line, filter.LocationId, filter.LocationType)))
            .ToList();
        var directBalances = accounts.ToDictionary(
            account => account.Id,
            account =>
            {
                var lines = matchingLines.Where(line => line.AccountId == account.Id).ToList();
                var debit = lines.Sum(line => line.DebitAmount);
                var credit = lines.Sum(line => line.CreditAmount);
                return new BalanceAmounts(
                    CalculateNormalBalance(account, debit, credit),
                    CalculateStatementBalance(account, debit, credit));
            });
        var accountsByParent = accounts
            .GroupBy(account => account.ParentAccountId ?? 0)
            .ToDictionary(group => group.Key, group => group.ToList());

        var assetRoots = BuildBalanceSheetAccounts(
            0,
            ChartAccountTypes.Asset,
            accountsByParent,
            directBalances,
            configuration);
        var liabilityRoots = BuildBalanceSheetAccounts(
            0,
            ChartAccountTypes.Liability,
            accountsByParent,
            directBalances,
            configuration);
        var equityRoots = BuildBalanceSheetAccounts(
            0,
            ChartAccountTypes.Equity,
            accountsByParent,
            directBalances,
            configuration);
        var currentYearEarnings = await GetCurrentYearEarningsAsync(
            businessId,
            asOfDate,
            filter,
            cancellationToken);
        var unclosedPriorYearEarnings = await GetUnclosedPriorYearEarningsAsync(
            businessId,
            asOfDate,
            filter,
            cancellationToken);
        var assets = assetRoots.Sum(account => account.RollupStatementBalance);
        var liabilities = liabilityRoots.Sum(account => account.RollupStatementBalance);
        var equityAccounts = equityRoots.Sum(account => account.RollupStatementBalance);
        var equity = equityAccounts + currentYearEarnings + unclosedPriorYearEarnings;
        var hasOpeningBalance = await dbContext.JournalEntries
            .AsNoTracking()
            .AnyAsync(journal => journal.BusinessId == businessId
                && journal.JournalType == AccountingJournalTypes.OpeningBalance
                && journal.Status == JournalEntryStatuses.Posted
                && journal.PostingDate <= asOfDate, cancellationToken);

        return new BalanceSheetReportReadModel(
            asOfDate,
            filter.LocationId.HasValue || !string.IsNullOrWhiteSpace(filter.LocationType),
            hasOpeningBalance
                ? null
                : "No posted opening balance exists on or before the selected date; this statement is limited to journal history available after accounting cutover.",
            new BalanceSheetSectionReadModel("Assets", assets, assetRoots),
            new BalanceSheetSectionReadModel("Liabilities", liabilities, liabilityRoots),
            new BalanceSheetSectionReadModel("Equity", equity, equityRoots),
            currentYearEarnings,
            unclosedPriorYearEarnings,
            assets,
            liabilities + equity,
            assets - (liabilities + equity));
    }

    private async Task<decimal> GetCurrentYearEarningsAsync(
        long businessId,
        DateTime asOfDate,
        BalanceSheetReportFilter filter,
        CancellationToken cancellationToken)
    {
        var year = await dbContext.FinancialYears
            .AsNoTracking()
            .SingleOrDefaultAsync(year => year.BusinessId == businessId
                && year.StartDate <= asOfDate
                && year.EndDate >= asOfDate, cancellationToken);
        return year is null
            ? 0m
            : await GetEarningsAsync(
                businessId,
                year.StartDate,
                asOfDate,
                filter,
                cancellationToken);
    }

    private async Task<decimal> GetUnclosedPriorYearEarningsAsync(
        long businessId,
        DateTime asOfDate,
        BalanceSheetReportFilter filter,
        CancellationToken cancellationToken)
    {
        var currentYear = await dbContext.FinancialYears
            .AsNoTracking()
            .SingleOrDefaultAsync(year => year.BusinessId == businessId
                && year.StartDate <= asOfDate
                && year.EndDate >= asOfDate, cancellationToken);
        if (currentYear is null)
            return 0m;

        var closedYearIds = await dbContext.JournalEntries
            .AsNoTracking()
            .Where(journal => journal.BusinessId == businessId
                && journal.JournalType == AccountingJournalTypes.YearEndClosing
                && journal.ReferenceType == AccountingReferenceTypes.FinancialYearClose
                && journal.Status == JournalEntryStatuses.Posted)
            .Select(journal => journal.ReferenceId)
            .ToListAsync(cancellationToken);
        var years = await dbContext.FinancialYears
            .AsNoTracking()
            .Where(year => year.BusinessId == businessId
                && year.EndDate < currentYear.StartDate
                && !closedYearIds.Contains(year.Id.ToString()))
            .OrderBy(year => year.StartDate)
            .ToListAsync(cancellationToken);
        decimal earnings = 0m;
        foreach (var year in years)
        {
            earnings += await GetEarningsAsync(
                businessId,
                year.StartDate,
                year.EndDate,
                filter,
                cancellationToken);
        }

        return earnings;
    }

    private async Task<decimal> GetEarningsAsync(
        long businessId,
        DateTime dateFrom,
        DateTime dateTo,
        BalanceSheetReportFilter filter,
        CancellationToken cancellationToken)
    {
        var journals = await HistoricallyPostedJournalsQuery(businessId, dateTo.Date)
            .Include(journal => journal.Lines)
                .ThenInclude(line => line.Account)
            .Where(journal => journal.PostingDate >= dateFrom.Date
                && journal.ReferenceType != AccountingReferenceTypes.FinancialYearClose)
            .ToListAsync(cancellationToken);
        var lines = journals
            .SelectMany(journal => journal.Lines.Where(line =>
                MatchesLocation(journal, line, filter.LocationId, filter.LocationType)))
            .ToList();
        var income = lines
            .Where(line => line.Account.AccountType == ChartAccountTypes.Income)
            .Sum(line => line.CreditAmount - line.DebitAmount);
        var expenses = lines
            .Where(line => line.Account.AccountType == ChartAccountTypes.Expense
                || line.Account.AccountType == ChartAccountTypes.CostOfGoodsSold)
            .Sum(line => line.DebitAmount - line.CreditAmount);
        return income - expenses;
    }

    private List<BalanceSheetAccountReadModel> BuildBalanceSheetAccounts(
        long parentId,
        string accountType,
        IReadOnlyDictionary<long, List<ChartAccount>> accountsByParent,
        IReadOnlyDictionary<long, BalanceAmounts> directBalances,
        AccountingConfiguration configuration)
    {
        if (!accountsByParent.TryGetValue(parentId, out var accounts))
            return [];

        return accounts
            .Where(account => string.Equals(
                account.AccountType,
                accountType,
                StringComparison.OrdinalIgnoreCase))
            .OrderBy(account => account.DisplayOrder)
            .ThenBy(account => account.AccountCode)
            .Select(account =>
            {
                var children = BuildBalanceSheetAccounts(
                    account.Id,
                    accountType,
                    accountsByParent,
                    directBalances,
                    configuration);
                var direct = directBalances[account.Id];
                return new BalanceSheetAccountReadModel(
                    account.Id,
                    account.AccountCode,
                    account.AccountName,
                    account.AccountType,
                    account.NormalBalanceType,
                    account.Id == configuration.OwnerCapitalAccountId ? "OwnerCapital"
                        : account.Id == configuration.OwnerDrawingsAccountId ? "OwnerDrawings"
                        : account.Id == configuration.RetainedEarningsAccountId ? "RetainedEarnings"
                        : null,
                    direct.Normal,
                    direct.Statement,
                    direct.Normal + children.Sum(child => child.RollupNormalBalance),
                    direct.Statement + children.Sum(child => child.RollupStatementBalance),
                    children);
            })
            .ToList();
    }

    private IQueryable<JournalEntry> HistoricallyPostedJournalsQuery(long businessId, DateTime asOfDate) =>
        dbContext.JournalEntries
            .AsNoTracking()
            .Where(journal => journal.BusinessId == businessId
                && (journal.Status == JournalEntryStatuses.Posted
                    || (journal.Status == JournalEntryStatuses.Reversed
                        && journal.ReversedByJournalEntryId.HasValue))
                && journal.PostingDate <= asOfDate.Date.AddDays(1).AddTicks(-1));

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

    private static decimal CalculateNormalBalance(ChartAccount account, decimal debit, decimal credit) =>
        string.Equals(account.NormalBalanceType, NormalBalanceTypes.Credit, StringComparison.OrdinalIgnoreCase)
            ? credit - debit
            : debit - credit;

    private static decimal CalculateStatementBalance(ChartAccount account, decimal debit, decimal credit)
    {
        var naturalBalance = CalculateNormalBalance(account, debit, credit);
        return account.AccountType == ChartAccountTypes.Asset
            ? account.NormalBalanceType == NormalBalanceTypes.Debit ? naturalBalance : -naturalBalance
            : account.NormalBalanceType == NormalBalanceTypes.Credit ? naturalBalance : -naturalBalance;
    }

    private sealed record BalanceAmounts(decimal Normal, decimal Statement);
}
