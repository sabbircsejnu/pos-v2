using RetailPOS.Application.Pos;
using RetailPOS.Application.Settings;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.SalesReports;
using RetailPOS.Application.SalesReports.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.SalesReports;

public sealed class SalesReportReadStore(RetailPOSDbContext dbContext) : ISalesReportReadStore
{
    public async Task<MobileSalesDashboardReadModel> GetMobileSalesDashboardAsync(
        SalesReportReadScope scope,
        SalesReportFilter filter,
        CancellationToken cancellationToken)
    {
        var sales = FilterMobileEligibleSales(scope, filter);
        var totals = await sales
            .GroupBy(_ => 1)
            .Select(group => new
            {
                TotalRevenue = group.Sum(sale => sale.TotalAmount),
                InvoiceCount = group.Count()
            })
            .FirstOrDefaultAsync(cancellationToken);

        var totalRevenue = totals?.TotalRevenue ?? 0m;
        var invoiceCount = totals?.InvoiceCount ?? 0;
        var eligibleSaleIds = await sales
            .Select(sale => sale.Id)
            .ToListAsync(cancellationToken);
        var saleIds = await dbContext.Sales
            .AsNoTracking()
            .Where(sale =>
                scope.OutletIds.Contains(sale.OutletId)
                && (sale.Status.ToLower() == "refunded" || sale.Status.ToLower() == "partially_refunded")
                && (!filter.CashierId.HasValue || sale.CashierId == filter.CashierId.Value))
            .Select(sale => sale.Id)
            .ToListAsync(cancellationToken);

        decimal refundAmount = 0m;
        decimal returnedUnits = 0m;
        if (saleIds.Count > 0)
        {
            // A refund transaction has its own journal reference. Sales refunded before that keep the sale id.
            var refundRows = await dbContext.Set<RetailPOS.Core.Entities.Uom.SaleRefund>()
                .AsNoTracking()
                .Where(refund => saleIds.Contains(refund.SaleId))
                .Select(refund => new { refund.SaleId, refund.Sequence })
                .ToListAsync(cancellationToken);
            var saleIdReferences = saleIds
                .Select(id => id.ToString())
                .Concat(refundRows
                    .Where(refund => refund.Sequence > 1)
                    .Select(refund => RetailPOS.Application.Sales.FinancialPostings.SaleFinancialIntentFactory.RefundJournalReference(refund.SaleId, refund.Sequence)))
                .ToList();
            var postedRefundLines = dbContext.JournalEntryLines
                .AsNoTracking()
                .Where(line =>
                    line.Description == "Sales return"
                    && line.JournalEntry.JournalType == "SalesReturnPosting"
                    && line.JournalEntry.ReferenceType == "SaleRefund"
                    && saleIdReferences.Contains(line.JournalEntry.ReferenceId)
                    && line.JournalEntry.Status == JournalEntryStatuses.Posted
                    && line.JournalEntry.PostedAt.HasValue
                    && line.JournalEntry.OutletId.HasValue
                    && scope.OutletIds.Contains(line.JournalEntry.OutletId.Value));
            // Deliberately fails closed (tested): net revenue is never shown while a refund's journal is unposted.
            var postedRefundReferences = await postedRefundLines
                .Select(line => line.JournalEntry.ReferenceId)
                .Distinct()
                .ToListAsync(cancellationToken);
            if (saleIdReferences.Except(postedRefundReferences).Any())
            {
                throw new InvalidDataException(
                    "A refunded sale in the authorized Outlet scope has no posted Sales return journal.");
            }

            refundAmount = await postedRefundLines
                .Where(line =>
                    line.JournalEntry.PostedAt >= filter.StartDate
                    && line.JournalEntry.PostedAt <= filter.EndDate)
                .SumAsync(line => (decimal?)line.DebitAmount, cancellationToken) ?? 0m;

            // Units sold count pieces only, so only pieces come off them; ml or kg returned are not pieces.
            var measuredVariants = await dbContext.VariantUomConfigs
                .AsNoTracking()
                .Where(config => config.EffectiveToUtc == null && config.BaseUnit.Code != "pcs")
                .Select(config => config.VariantId)
                .ToListAsync(cancellationToken);
            returnedUnits = await dbContext.StockLedgers
                .AsNoTracking()
                .Where(movement =>
                    !measuredVariants.Contains(movement.VariantId)
                    && movement.TransactionType == StockLedgerTransactionType.Return
                    && movement.ReferenceType == StockLedgerReferenceType.Sale
                    && saleIds.Contains(movement.ReferenceId)
                    && scope.OutletIds.Contains(movement.LocationId)
                    && movement.CreatedAt >= filter.StartDate
                    && movement.CreatedAt <= filter.EndDate)
                .SumAsync(movement => (decimal?)movement.QtyIn, cancellationToken) ?? 0m;
        }

        var unitsSold = await dbContext.SaleItems
            .AsNoTracking()
            .Where(item => eligibleSaleIds.Contains(item.SaleId))
            .SumAsync(item => (decimal?)(item.UomBaseUnitCode == null || item.UomBaseUnitCode == "pcs" ? item.Quantity : 0m), cancellationToken) ?? 0m;
        var netRevenue = totalRevenue - refundAmount;

        return new MobileSalesDashboardReadModel(
            filter.StartDate,
            filter.EndDate,
            totalRevenue,
            netRevenue,
            invoiceCount,
            // Normalized: a SQL COALESCE(..., 0.0) carries a scale that mobile clients would read as a non-integer.
            RetailPOS.Application.Uom.UomQuantityMath.Normalize(unitsSold - returnedUnits),
            invoiceCount > 0 ? netRevenue / invoiceCount : 0m);
    }

    public async Task<IReadOnlyList<MobileDailySalesActivityReadModel>> GetMobileDailySalesActivityAsync(
        SalesReportReadScope scope,
        SalesReportFilter filter,
        CancellationToken cancellationToken)
    {
        var grouped = await FilterMobileEligibleSales(scope, filter)
            .GroupBy(sale => (sale.SaleDateTime ?? sale.SaleDate).Date)
            .Select(group => new
            {
                DateUtc = group.Key,
                InvoiceCount = group.Count()
            })
            .OrderBy(group => group.DateUtc)
            .ToListAsync(cancellationToken);

        return grouped
            .Select(group => new MobileDailySalesActivityReadModel(group.DateUtc, group.InvoiceCount))
            .ToArray();
    }

    public async Task<SalesReportSummaryReadModel> GetSummaryAsync(
        SalesReportReadScope scope,
        SalesReportFilter filter,
        CancellationToken cancellationToken)
    {
        var query = FilterSales(scope, filter);
        if (!string.IsNullOrWhiteSpace(filter.PaymentMethod))
        {
            query = query.Where(sale => sale.PaymentMethod == filter.PaymentMethod);
        }

        var totals = await query
            .GroupBy(_ => 1)
            .Select(group => new
            {
                TotalRevenue = group.Sum(sale => sale.TotalAmount),
                TotalDiscount = group.Sum(sale => sale.Discount),
                TotalTax = group.Sum(sale => sale.Tax),
                TotalTransactions = group.Count()
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (totals is null)
        {
            return new SalesReportSummaryReadModel(0, 0, 0, 0, 0, 0);
        }

        var netRevenue = totals.TotalRevenue - totals.TotalDiscount;
        return new SalesReportSummaryReadModel(
            totals.TotalRevenue,
            totals.TotalDiscount,
            totals.TotalTax,
            netRevenue,
            totals.TotalTransactions,
            totals.TotalTransactions > 0 ? netRevenue / totals.TotalTransactions : 0);
    }

    public async Task<decimal> GetUnitCountAsync(
        SalesReportReadScope scope,
        SalesReportFilter filter,
        CancellationToken cancellationToken)
    {
        return await dbContext.SaleItems
            .AsNoTracking()
            .Where(item =>
                scope.OutletIds.Contains(item.Sale.OutletId)
                && item.Sale.SaleDate >= filter.StartDate
                && item.Sale.SaleDate <= filter.EndDate
                && (!filter.OutletId.HasValue || item.Sale.OutletId == filter.OutletId.Value)
                && (!filter.CashierId.HasValue || item.Sale.CashierId == filter.CashierId.Value))
            .SumAsync(item => (decimal?)(item.UomBaseUnitCode == null || item.UomBaseUnitCode == "pcs" ? item.Quantity : 0m), cancellationToken) ?? 0m;
    }

    public async Task<IReadOnlyList<TopProductReadModel>> GetTopProductsAsync(
        SalesReportReadScope scope,
        SalesReportFilter filter,
        CancellationToken cancellationToken)
    {
        var grouped = await dbContext.SaleItems
            .AsNoTracking()
            .Where(item =>
                scope.OutletIds.Contains(item.Sale.OutletId)
                && item.Sale.SaleDate >= filter.StartDate
                && item.Sale.SaleDate <= filter.EndDate
                && (!filter.OutletId.HasValue || item.Sale.OutletId == filter.OutletId.Value))
            .GroupBy(item => item.VariantId)
            .Select(group => new
            {
                VariantId = group.Key,
                TotalQuantity = group.Sum(item => item.Quantity),
                TotalRevenue = group.Sum(item => item.Subtotal)
            })
            .OrderByDescending(item => item.TotalQuantity)
            .Take(filter.Limit)
            .ToListAsync(cancellationToken);

        var variantIds = grouped.Select(item => item.VariantId).ToList();
        var variantMap = await dbContext.ProductVariants
            .AsNoTracking()
            .Where(variant => variantIds.Contains(variant.Id))
            .Select(variant => new { variant.Id, variant.Sku, ProductName = variant.Product.Name })
            .ToDictionaryAsync(variant => variant.Id, cancellationToken);

        return grouped.Select(item =>
        {
            variantMap.TryGetValue(item.VariantId, out var variant);
            return new TopProductReadModel(
                item.VariantId,
                variant?.ProductName ?? string.Empty,
                variant?.Sku ?? string.Empty,
                item.TotalQuantity,
                item.TotalRevenue);
        }).ToArray();
    }

    public async Task<IReadOnlyList<SalesByOutletReadModel>> GetByOutletAsync(
        SalesReportReadScope scope,
        SalesReportFilter filter,
        CancellationToken cancellationToken)
    {
        var grouped = await dbContext.Sales
            .AsNoTracking()
            .Where(sale =>
                scope.OutletIds.Contains(sale.OutletId)
                && sale.SaleDate >= filter.StartDate
                && sale.SaleDate <= filter.EndDate)
            .GroupBy(sale => sale.OutletId)
            .Select(group => new
            {
                OutletId = group.Key,
                TransactionCount = group.Count(),
                TotalRevenue = group.Sum(sale => sale.TotalAmount)
            })
            .ToListAsync(cancellationToken);

        var outletIds = grouped.Select(item => item.OutletId).ToList();
        var outletNames = await dbContext.Outlets
            .AsNoTracking()
            .Where(outlet => outletIds.Contains(outlet.Id))
            .ToDictionaryAsync(outlet => outlet.Id, outlet => outlet.Name, cancellationToken);

        return grouped
            .Select(item => new SalesByOutletReadModel(
                item.OutletId,
                outletNames.GetValueOrDefault(item.OutletId, string.Empty),
                item.TransactionCount,
                item.TotalRevenue))
            .OrderByDescending(item => item.TotalRevenue)
            .ToArray();
    }

    public async Task<IReadOnlyList<SalesByPaymentMethodReadModel>> GetByPaymentMethodAsync(
        SalesReportReadScope scope,
        SalesReportFilter filter,
        CancellationToken cancellationToken)
    {
        var grouped = await FilterSales(scope, filter)
            .GroupBy(sale => sale.PaymentMethod)
            .Select(group => new
            {
                PaymentMethod = group.Key,
                Count = group.Count(),
                Amount = group.Sum(sale => sale.TotalAmount)
            })
            .OrderByDescending(item => item.Amount)
            .ToListAsync(cancellationToken);

        return grouped.Select(item =>
            new SalesByPaymentMethodReadModel(item.PaymentMethod, item.Count, item.Amount)).ToArray();
    }

    public async Task<IReadOnlyList<DailySalesTrendReadModel>> GetDailyTrendAsync(
        SalesReportReadScope scope,
        SalesReportFilter filter,
        CancellationToken cancellationToken)
    {
        var rows = await FilterSales(scope, filter)
            .Select(sale => new { sale.SaleDate, sale.TotalAmount })
            .ToListAsync(cancellationToken);
        var zone = CashSessionTime.ResolveEffectiveTimeZoneId(null, filter.TimeZoneId ?? string.Empty);

        // Calendar days in the Company zone cannot be grouped by the UTC date in SQL.
        return rows
            .GroupBy(row => CompanyDates.DateFor(row.SaleDate, zone))
            .OrderBy(group => group.Key)
            .Select(group => new DailySalesTrendReadModel(
                group.Key,
                group.Count(),
                group.Sum(row => row.TotalAmount)))
            .ToArray();

    }

    private IQueryable<Core.Entities.Sale> FilterSales(
        SalesReportReadScope scope,
        SalesReportFilter filter)
    {
        var query = dbContext.Sales
            .AsNoTracking()
            .Where(sale =>
                scope.OutletIds.Contains(sale.OutletId)
                && sale.SaleDate >= filter.StartDate
                && sale.SaleDate <= filter.EndDate
                && (!filter.CashierId.HasValue || sale.CashierId == filter.CashierId.Value));

        if (filter.OutletId.HasValue)
        {
            query = query.Where(sale => sale.OutletId == filter.OutletId.Value);
        }

        return query;
    }

    private IQueryable<Core.Entities.Sale> FilterMobileEligibleSales(
        SalesReportReadScope scope,
        SalesReportFilter filter) =>
        dbContext.Sales
            .AsNoTracking()
            .Where(sale =>
                scope.OutletIds.Contains(sale.OutletId)
                && (sale.Status.ToLower() == "completed" || sale.Status.ToLower() == "refunded" || sale.Status.ToLower() == "partially_refunded")
                && (sale.SaleDateTime ?? sale.SaleDate) >= filter.StartDate
                && (sale.SaleDateTime ?? sale.SaleDate) <= filter.EndDate
                && (!filter.OutletId.HasValue || sale.OutletId == filter.OutletId.Value)
                && (!filter.CashierId.HasValue || sale.CashierId == filter.CashierId.Value));
}
