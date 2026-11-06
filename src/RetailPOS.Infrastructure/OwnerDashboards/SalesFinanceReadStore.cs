using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.OwnerDashboards;
using RetailPOS.Application.Settings;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.OwnerDashboards;

public sealed class SalesFinanceReadStore(RetailPOSDbContext dbContext) : ISalesFinanceReadStore
{
    public Task<SalesFacts> GetSalesFactsAsync(
        OwnerDashboardScope scope,
        List<long> outletIds,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken) =>
        outletIds.Count == 0
            ? Task.FromResult(new SalesFacts([], 0, 0))
            : OwnerDashboardSales.InReadOnlySnapshotAsync(
                dbContext,
                () => ReadSalesFactsAsync(scope, outletIds, from, to, cancellationToken),
                cancellationToken);

    public async Task<decimal> GetPurchaseValueAsync(
        OwnerDashboardScope scope,
        List<long> outletIds,
        List<long> warehouseIds,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        var (startUtc, endUtc) = CompanyDates.DayWindow(from, to, scope.TimeZoneId);
        return await dbContext.GrnItems
            .AsNoTracking()
            .Where(item =>
                item.Grn.Status == "completed"
                && item.Grn.ReceivedDate >= startUtc
                && item.Grn.ReceivedDate < endUtc
                && ((item.Grn.PurchaseOrder.OutletId != null
                        && outletIds.Contains(item.Grn.PurchaseOrder.OutletId.Value))
                    || (item.Grn.PurchaseOrder.WarehouseId != null
                        && warehouseIds.Contains(item.Grn.PurchaseOrder.WarehouseId.Value))))
            .SumAsync(item => (decimal?)(item.TaxableAmount ?? item.ReceivedQty * item.UnitCost), cancellationToken) ?? 0m;
    }

    public async Task<decimal> GetApprovedExpenseValueAsync(
        OwnerDashboardScope scope,
        List<long> outletIds,
        List<long> warehouseIds,
        bool includeBusinessLevel,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        var (startUtc, endUtc) = CompanyDates.DayWindow(from, to, scope.TimeZoneId);
        return await ApprovedExpenses(scope.BusinessId, outletIds, warehouseIds, includeBusinessLevel)
            .Where(expense => expense.ExpenseDate >= startUtc && expense.ExpenseDate < endUtc)
            .SumAsync(expense => (decimal?)(expense.TaxableAmount ?? expense.Amount), cancellationToken) ?? 0m;
    }

    public async Task<IReadOnlyList<CashFlowDayFact>> GetCashFlowAsync(
        OwnerDashboardScope scope,
        List<long> salesOutletIds,
        List<long> expenseOutletIds,
        List<long> expenseWarehouseIds,
        bool includeBusinessLevel,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken) =>
        await OwnerDashboardSales.InReadOnlySnapshotAsync<IReadOnlyList<CashFlowDayFact>>(
            dbContext,
            async () =>
            {
                var received = await ReadCashReceivedAsync(scope, salesOutletIds, from, to, cancellationToken);
                var paid = await ReadExpensePaymentsAsync(
                    scope,
                    expenseOutletIds,
                    expenseWarehouseIds,
                    includeBusinessLevel,
                    from,
                    to,
                    cancellationToken);
                return received.Keys.Union(paid.Keys)
                    .OrderBy(date => date)
                    .Select(date => new CashFlowDayFact(
                        date,
                        received.GetValueOrDefault(date),
                        paid.GetValueOrDefault(date)))
                    .ToList();
            },
            cancellationToken);

    private async Task<SalesFacts> ReadSalesFactsAsync(
        OwnerDashboardScope scope,
        List<long> outletIds,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        var zone = scope.TimeZoneId;
        var sales = OwnerDashboardSales.InPeriod(dbContext, scope.BusinessId, outletIds, from, to, zone);

        var saleTotals = await sales
            .GroupBy(sale => new
            {
                sale.OutletId,
                Date = sale.BusinessDate ?? TimeZoneInfo.ConvertTimeBySystemTimeZoneId(sale.SaleDate, zone).Date
            })
            .Select(group => new
            {
                group.Key.OutletId,
                group.Key.Date,
                NetSales = group.Sum(sale => sale.TotalAmount - sale.Tax)
            })
            .ToListAsync(cancellationToken);

        // Each sale's COGS is rounded to currency before it is added up, exactly as its journal snapshot rounds it
        // (SaleFinancialIntentFactory.ForSale). PostgreSQL rounds numeric half away from zero, the same as MonetaryRounding.
        var costTotals = await sales
            .Select(sale => new
            {
                sale.OutletId,
                Date = sale.BusinessDate ?? TimeZoneInfo.ConvertTimeBySystemTimeZoneId(sale.SaleDate, zone).Date,
                Cogs = Math.Round(sale.Items.Sum(item => (item.HistoricalUnitCost ?? 0m) * item.Quantity), 2)
            })
            .GroupBy(sale => new { sale.OutletId, sale.Date })
            .Select(group => new { group.Key.OutletId, group.Key.Date, Cogs = group.Sum(sale => sale.Cogs) })
            .ToListAsync(cancellationToken);

        var lineTotals = await sales
            .SelectMany(sale => sale.Items.Select(item => new
            {
                sale.OutletId,
                Date = sale.BusinessDate ?? TimeZoneInfo.ConvertTimeBySystemTimeZoneId(sale.SaleDate, zone).Date,
                item.Quantity,
                item.HistoricalUnitCost
            }))
            .GroupBy(line => new { line.OutletId, line.Date })
            .Select(group => new
            {
                group.Key.OutletId,
                group.Key.Date,
                Units = group.Sum(line => (long)line.Quantity),
                MissingCost = group.Count(line => line.HistoricalUnitCost == null || line.HistoricalUnitCost == 0m)
            })
            .ToListAsync(cancellationToken);

        var days = new Dictionary<(long Outlet, DateOnly Date), SalesDayFact>();
        foreach (var row in saleTotals)
        {
            (long Outlet, DateOnly Date) key = (row.OutletId, DateOnly.FromDateTime(row.Date));
            days[key] = Merge(days, key) with { NetSales = Merge(days, key).NetSales + row.NetSales };
        }

        foreach (var row in costTotals)
        {
            (long Outlet, DateOnly Date) key = (row.OutletId, DateOnly.FromDateTime(row.Date));
            days[key] = Merge(days, key) with { Cogs = Merge(days, key).Cogs + row.Cogs };
        }

        foreach (var row in lineTotals)
        {
            (long Outlet, DateOnly Date) key = (row.OutletId, DateOnly.FromDateTime(row.Date));
            var day = Merge(days, key);
            days[key] = day with
            {
                NetUnits = day.NetUnits + row.Units,
                MissingCostLines = day.MissingCostLines + row.MissingCost
            };
        }

        var reversals = await OwnerDashboardSales.ReversalsAsync(
            dbContext,
            scope.BusinessId,
            outletIds,
            from,
            to,
            zone,
            cancellationToken);
        foreach (var reversal in reversals.Where(reversal => reversal.Trusted))
        {
            (long Outlet, DateOnly Date) key = (reversal.OutletId, reversal.AccountingDate);
            var day = Merge(days, key);
            days[key] = day with
            {
                NetSales = day.NetSales - reversal.Snapshot.RefundAmount,
                NetUnits = day.NetUnits - reversal.Snapshot.Lines.Sum(line => (long)line.Quantity),
                Cogs = day.Cogs - reversal.Snapshot.Cost
            };
        }

        var refundedWithoutRecord = await sales.CountAsync(
            sale => sale.Status.ToLower() == "refunded"
                && !dbContext.SaleFinancialPostings.Any(posting =>
                    posting.SaleId == sale.Id && posting.Kind == SaleFinancialPostingKinds.Reversal),
            cancellationToken);
        var unreversedExchanges = await sales.CountAsync(
            sale => sale.Status.ToLower() == "completed" && sale.ReplacementSaleId != null,
            cancellationToken);

        return new SalesFacts(
            days.Values.OrderBy(day => day.BusinessDate).ThenBy(day => day.OutletId).ToList(),
            reversals.Count(reversal => !reversal.Trusted) + refundedWithoutRecord,
            unreversedExchanges);
    }

    private static SalesDayFact Merge(
        Dictionary<(long Outlet, DateOnly Date), SalesDayFact> days,
        (long Outlet, DateOnly Date) key) =>
        days.GetValueOrDefault(key) ?? new SalesDayFact(key.Outlet, key.Date, 0m, 0L, 0m, 0);

    /// <summary>Tender actually collected, net of reversal allocations. Handovers and deposits are never read.</summary>
    private async Task<Dictionary<DateOnly, decimal>> ReadCashReceivedAsync(
        OwnerDashboardScope scope,
        List<long> outletIds,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<DateOnly, decimal>();
        if (outletIds.Count == 0)
            return result;

        var zone = scope.TimeZoneId;
        var collected = await OwnerDashboardSales.InPeriod(dbContext, scope.BusinessId, outletIds, from, to, zone)
            .SelectMany(sale => sale.Payments
                .Where(payment => payment.Method.ToLower() != "loyalty")
                .Select(payment => new
                {
                    Date = sale.BusinessDate ?? TimeZoneInfo.ConvertTimeBySystemTimeZoneId(sale.SaleDate, zone).Date,
                    payment.Amount
                }))
            .GroupBy(payment => payment.Date)
            .Select(group => new { Date = group.Key, Amount = group.Sum(payment => payment.Amount) })
            .ToListAsync(cancellationToken);
        foreach (var row in collected)
            Add(result, DateOnly.FromDateTime(row.Date), row.Amount);

        var reversals = await OwnerDashboardSales.ReversalsAsync(
            dbContext,
            scope.BusinessId,
            outletIds,
            from,
            to,
            zone,
            cancellationToken);
        foreach (var reversal in reversals.Where(reversal => reversal.Trusted))
        {
            var returned = reversal.Snapshot.Payments
                .Where(payment => !string.Equals(payment.Method, "loyalty", StringComparison.OrdinalIgnoreCase))
                .Sum(payment => payment.Amount);
            Add(result, reversal.AccountingDate, -returned);
        }

        return result;
    }

    /// <summary>
    /// Gross cash out. Bank and petty cash are paid when approved, on the expense date; a POS drawer expense is paid only
    /// when actually disbursed, and a drawer reversal returns the cash on the day it is recorded.
    /// </summary>
    private async Task<Dictionary<DateOnly, decimal>> ReadExpensePaymentsAsync(
        OwnerDashboardScope scope,
        List<long> outletIds,
        List<long> warehouseIds,
        bool includeBusinessLevel,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<DateOnly, decimal>();
        var zone = scope.TimeZoneId;
        var (startUtc, endUtc) = CompanyDates.DayWindow(from, to, zone);
        var approved = ApprovedExpenses(scope.BusinessId, outletIds, warehouseIds, includeBusinessLevel);

        var direct = await approved
            .Where(expense =>
                expense.PaymentSource != ExpensePaymentSources.PosDrawer
                && expense.ExpenseDate >= startUtc
                && expense.ExpenseDate < endUtc)
            .GroupBy(expense => expense.ExpenseDate)
            .Select(group => new { Instant = group.Key, Amount = group.Sum(expense => expense.Amount) })
            .ToListAsync(cancellationToken);
        foreach (var row in direct)
            Add(result, DateOnly.FromDateTime(CompanyDates.DateFor(row.Instant, zone)), row.Amount);

        var disbursed = await approved
            .Where(expense =>
                expense.PaymentSource == ExpensePaymentSources.PosDrawer
                && expense.DisbursedAtUtc != null
                && expense.DisbursedAtUtc >= startUtc
                && expense.DisbursedAtUtc < endUtc)
            .Select(expense => new { Instant = expense.DisbursedAtUtc!.Value, expense.Amount })
            .ToListAsync(cancellationToken);
        foreach (var row in disbursed)
            Add(result, DateOnly.FromDateTime(CompanyDates.DateFor(row.Instant, zone)), row.Amount);

        if (outletIds.Count > 0)
        {
            var reversals = await dbContext.CashSessionMovements
                .AsNoTracking()
                .Where(movement =>
                    movement.BusinessId == scope.BusinessId
                    && movement.MovementType == CashSessionMovementTypes.DrawerExpenseReversal
                    && outletIds.Contains(movement.OutletId)
                    && movement.RecordedAtUtc >= startUtc
                    && movement.RecordedAtUtc < endUtc)
                .Select(movement => new { movement.RecordedAtUtc, movement.CashEffect })
                .ToListAsync(cancellationToken);
            foreach (var row in reversals)
                Add(result, DateOnly.FromDateTime(CompanyDates.DateFor(row.RecordedAtUtc, zone)), -Math.Abs(row.CashEffect));
        }

        return result;
    }

    private IQueryable<Expense> ApprovedExpenses(
        long businessId,
        List<long> outletIds,
        List<long> warehouseIds,
        bool includeBusinessLevel) =>
        dbContext.Expenses
            .AsNoTracking()
            .Where(expense =>
                expense.BusinessId == businessId
                && expense.Status == ExpenseStatuses.Approved
                && ((expense.OutletId != null && outletIds.Contains(expense.OutletId.Value))
                    || (expense.WarehouseId != null && warehouseIds.Contains(expense.WarehouseId.Value))
                    || (includeBusinessLevel && expense.OutletId == null && expense.WarehouseId == null)));

    private static void Add(Dictionary<DateOnly, decimal> totals, DateOnly date, decimal amount) =>
        totals[date] = totals.GetValueOrDefault(date) + amount;
}
