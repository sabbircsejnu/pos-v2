using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.AccountingReports.Abstractions;
using RetailPOS.Application.AccountingReports.ReadModels;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.AccountingReports;

public sealed class CogsReportReadStore(RetailPOSDbContext dbContext) : ICogsReportReadStore
{
    public async Task<CogsReportReadModel> GetAsync(
        CogsReportFilter filter,
        long businessId,
        CancellationToken cancellationToken)
    {
        var configuration = await dbContext.AccountingConfigurations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.BusinessId == businessId,
                cancellationToken);
        if (configuration?.CogsAccountId is null
            || configuration.InventoryAccountId is null)
            throw new InvalidOperationException(
                "COGS and Inventory accounts must be configured before running valuation reports.");

        var cogsAccountId = configuration.CogsAccountId.Value;
        var journalQuery = ApplyScopeAndPeriod(
                dbContext.JournalEntries
                    .AsNoTracking()
                    .Where(journal => journal.BusinessId == businessId
                        && journal.Status == JournalEntryStatuses.Posted
                        && journal.ReferenceType != AccountingReferenceTypes.FinancialYearClose),
                filter)
            .Where(journal => journal.Lines.Any(line => line.AccountId == cogsAccountId));
        if (filter.ProductId.HasValue || filter.VariantId.HasValue)
        {
            var sales = dbContext.Sales.AsNoTracking().AsQueryable();
            if (filter.ProductId.HasValue)
                sales = sales.Where(sale => sale.Items.Any(item =>
                    item.Variant.ProductId == filter.ProductId.Value));
            if (filter.VariantId.HasValue)
                sales = sales.Where(sale => sale.Items.Any(item =>
                    item.VariantId == filter.VariantId.Value));
            var saleReferenceIds = (await sales
                .Select(sale => sale.Id.ToString())
                .ToListAsync(cancellationToken))
                .ToHashSet();
            journalQuery = journalQuery.Where(journal =>
                saleReferenceIds.Contains(journal.ReferenceId));
        }

        var journals = await journalQuery
            .Include(journal => journal.Lines)
            .OrderBy(journal => journal.PostingDate)
            .ThenBy(journal => journal.Id)
            .ToListAsync(cancellationToken);
        var rows = journals
            .SelectMany(journal => journal.Lines
                .Where(line => line.AccountId == cogsAccountId)
                .Select(line => new CogsReportRowReadModel(
                    journal.Id,
                    journal.JournalNumber,
                    journal.PostingDate,
                    journal.JournalType,
                    journal.ReferenceType,
                    journal.ReferenceId,
                    journal.ReferenceNumber,
                    line.OutletId ?? journal.OutletId,
                    line.WarehouseId ?? journal.WarehouseId,
                    line.DebitAmount,
                    line.CreditAmount,
                    line.DebitAmount - line.CreditAmount,
                    line.Description ?? journal.Description)))
            .ToList();
        var total = rows.Sum(row => row.NetCogs);
        var balanceJournals = await ApplyScopeAndPeriod(
                dbContext.JournalEntries
                    .AsNoTracking()
                    .Where(journal => journal.BusinessId == businessId
                        && journal.Status == JournalEntryStatuses.Posted),
                filter)
            .Include(journal => journal.Lines)
            .ToListAsync(cancellationToken);
        var glBalance = balanceJournals
            .SelectMany(journal => journal.Lines)
            .Where(line => line.AccountId == cogsAccountId)
            .Sum(line => line.DebitAmount - line.CreditAmount);
        return new CogsReportReadModel(
            rows,
            total,
            glBalance,
            total - glBalance,
            cogsAccountId);
    }

    private static IQueryable<JournalEntry> ApplyScopeAndPeriod(
        IQueryable<JournalEntry> query,
        CogsReportFilter filter)
    {
        if (filter.DateFrom.HasValue)
            query = query.Where(journal =>
                journal.PostingDate >= filter.DateFrom.Value.Date);

        var end = (filter.DateTo ?? DateTime.UtcNow)
            .Date
            .AddDays(1)
            .AddTicks(-1);
        query = query.Where(journal => journal.PostingDate <= end);
        if (filter.OutletId.HasValue)
            query = query.Where(journal => journal.OutletId == filter.OutletId.Value);
        if (filter.WarehouseId.HasValue)
            query = query.Where(journal => journal.WarehouseId == filter.WarehouseId.Value);
        return query;
    }
}
