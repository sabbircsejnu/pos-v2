using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.AccountingReports.Abstractions;
using RetailPOS.Application.AccountingReports.ReadModels;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.AccountingReports;

public sealed class ProductContributionReadStore(RetailPOSDbContext dbContext)
    : IProductContributionReadStore
{
    public async Task<ProductContributionReadModel> GetAsync(
        ProfitabilityFilter filter,
        long businessId,
        CancellationToken cancellationToken)
    {
        var dateFrom = filter.DateFrom!.Value.Date;
        var dateTo = filter.DateTo!.Value.Date;
        var outletIds = await dbContext.Outlets
            .AsNoTracking()
            .Where(outlet => outlet.BusinessId == businessId)
            .Select(outlet => outlet.Id)
            .ToListAsync(cancellationToken);
        var journals = await GetHistoricallyPostedJournalsQuery(businessId, dateTo)
            .Include(journal => journal.Lines)
            .ThenInclude(line => line.Account)
            .Where(journal => journal.PostingDate >= dateFrom
                && journal.ReferenceType != AccountingReferenceTypes.FinancialYearClose)
            .ToListAsync(cancellationToken);
        var saleJournals = journals
            .Where(journal => journal.ReferenceType == "Sale")
            .ToList();
        var saleIds = saleJournals
            .Select(journal => long.TryParse(journal.ReferenceId, out var saleId)
                ? saleId
                : (long?)null)
            .Where(saleId => saleId.HasValue)
            .Select(saleId => saleId!.Value)
            .ToList();
        var salesQuery = dbContext.Sales
            .AsNoTracking()
            .Include(sale => sale.Items)
                .ThenInclude(item => item.Variant)
                    .ThenInclude(variant => variant.Product)
                        .ThenInclude(product => product.Category)
            .Where(sale => outletIds.Contains(sale.OutletId)
                && saleIds.Contains(sale.Id));

        if (filter.LocationId.HasValue)
        {
            salesQuery = string.Equals(
                filter.LocationType,
                "outlet",
                StringComparison.OrdinalIgnoreCase)
                ? salesQuery.Where(sale => sale.OutletId == filter.LocationId.Value)
                : salesQuery.Where(_ => false);
        }

        var sales = await salesQuery.ToListAsync(cancellationToken);
        var sourceRows = sales
            .SelectMany(sale => sale.Items.Select(item =>
                new ProductContributionSourceRow(
                    item.Variant.Product.Id,
                    item.Variant.Product.Name,
                    item.Variant.Product.ProductCode,
                    item.Variant.Product.CategoryId,
                    item.Variant.Product.Category.Name,
                    item.Quantity * item.UnitPrice,
                    (item.HistoricalUnitCost ?? 0m) * item.Quantity)))
            .Where(row => (!filter.ProductId.HasValue
                    || row.ProductId == filter.ProductId.Value)
                && (!filter.CategoryId.HasValue
                    || row.CategoryId == filter.CategoryId.Value))
            .ToList();
        var products = sourceRows
            .GroupBy(row => new
            {
                row.ProductId,
                row.ProductName,
                row.ProductCode,
                row.CategoryId,
                row.CategoryName
            })
            .Select(group => BuildProductRow(
                group.Key.ProductId,
                group.Key.ProductName,
                group.Key.ProductCode,
                group.Key.CategoryId,
                group.Key.CategoryName,
                group.Sum(row => row.Revenue),
                group.Sum(row => row.CostOfGoodsSold)))
            .OrderBy(row => row.ProductName)
            .ToList();
        var categories = sourceRows
            .GroupBy(row => new { row.CategoryId, row.CategoryName })
            .Select(group => BuildCategoryRow(
                group.Key.CategoryId,
                group.Key.CategoryName,
                group.Sum(row => row.Revenue),
                group.Sum(row => row.CostOfGoodsSold)))
            .OrderBy(row => row.CategoryName)
            .ToList();
        var saleJournalLines = saleJournals
            .SelectMany(journal => journal.Lines)
            .ToList();
        var authoritativeRevenue = saleJournalLines
            .Where(line => line.Account.AccountType == ChartAccountTypes.Income)
            .Sum(line => line.CreditAmount - line.DebitAmount);
        var authoritativeCogs = saleJournalLines
            .Where(line => line.Account.AccountType == ChartAccountTypes.CostOfGoodsSold)
            .Sum(line => line.DebitAmount - line.CreditAmount);
        var attributedRevenue = products.Sum(row => row.Revenue);
        var attributedCogs = products.Sum(row => row.CostOfGoodsSold);

        return new ProductContributionReadModel(
            dateFrom,
            dateTo,
            "Revenue minus COGS only; shared expenses are not allocated.",
            products,
            categories,
            attributedRevenue,
            attributedCogs,
            attributedRevenue - attributedCogs,
            authoritativeRevenue,
            authoritativeCogs,
            authoritativeRevenue - attributedRevenue,
            authoritativeCogs - attributedCogs,
            attributedRevenue + (authoritativeRevenue - attributedRevenue)
                - authoritativeRevenue,
            attributedCogs + (authoritativeCogs - attributedCogs)
                - authoritativeCogs,
            "Contribution uses SaleItem revenue and historical unit cost evidence only; shared expenses and journal lines without product/category source evidence are not allocated.");
    }

    private IQueryable<JournalEntry> GetHistoricallyPostedJournalsQuery(
        long businessId,
        DateTime asOfDate) =>
        dbContext.JournalEntries
            .AsNoTracking()
            .Where(journal => journal.BusinessId == businessId
                && (journal.Status == JournalEntryStatuses.Posted
                    || (journal.Status == JournalEntryStatuses.Reversed
                        && journal.ReversedByJournalEntryId.HasValue))
                && journal.PostingDate
                    <= asOfDate.Date.AddDays(1).AddTicks(-1));

    private static ProductContributionRowReadModel BuildProductRow(
        long productId,
        string productName,
        string? productCode,
        long categoryId,
        string categoryName,
        decimal revenue,
        decimal costOfGoodsSold)
    {
        var contribution = revenue - costOfGoodsSold;
        return new ProductContributionRowReadModel(
            productId,
            productName,
            productCode,
            categoryId,
            categoryName,
            revenue,
            costOfGoodsSold,
            contribution,
            revenue == 0m
                ? 0m
                : Math.Round(
                    contribution / revenue * 100m,
                    2,
                    MidpointRounding.AwayFromZero));
    }

    private static CategoryContributionRowReadModel BuildCategoryRow(
        long categoryId,
        string categoryName,
        decimal revenue,
        decimal costOfGoodsSold)
    {
        var contribution = revenue - costOfGoodsSold;
        return new CategoryContributionRowReadModel(
            categoryId,
            categoryName,
            revenue,
            costOfGoodsSold,
            contribution,
            revenue == 0m
                ? 0m
                : Math.Round(
                    contribution / revenue * 100m,
                    2,
                    MidpointRounding.AwayFromZero));
    }

    private sealed record ProductContributionSourceRow(
        long ProductId,
        string ProductName,
        string? ProductCode,
        long CategoryId,
        string CategoryName,
        decimal Revenue,
        decimal CostOfGoodsSold);
}
