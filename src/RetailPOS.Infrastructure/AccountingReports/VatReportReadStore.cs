using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.VatReports.Abstractions;
using RetailPOS.Application.VatReports.ReadModels;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.PurchaseOrders;

namespace RetailPOS.Infrastructure.AccountingReports;

public sealed class VatReportReadStore(RetailPOSDbContext dbContext) : IVatReportReadStore
{
    private const string VatPayableSlot = "vat_payable";
    private const string VatReceivableSlot = "vat_receivable";

    public async Task<IReadOnlyList<VatTransactionReadModel>> GetTransactionsAsync(
        VatReportFilter filter,
        long businessId,
        CancellationToken cancellationToken)
    {
        var rows = await BuildEventsAsync(filter, businessId, cancellationToken);
        return rows
            .Where(transaction =>
                transaction.PostingDate >= filter.DateFrom &&
                transaction.PostingDate <= filter.DateTo.AddDays(1).AddTicks(-1) &&
                transaction.VatAmount != 0m &&
                (string.IsNullOrWhiteSpace(filter.VatDirection) ||
                    transaction.VatDirection.Equals(
                        filter.VatDirection,
                        StringComparison.OrdinalIgnoreCase)))
            .OrderBy(transaction => transaction.PostingDate)
            .ThenBy(transaction => transaction.SourceType)
            .ThenBy(transaction => transaction.SourceReference)
            .ToArray();
    }

    public async Task<VatSummaryReadModel> GetSummaryAsync(
        VatReportFilter filter,
        long businessId,
        CancellationToken cancellationToken)
    {
        var rows = await GetTransactionsAsync(filter, businessId, cancellationToken);
        var exceptions = await GetExceptionsAsync(filter, businessId, cancellationToken);
        var sales = rows.Where(row => row.SourceType == "Sale" && row.VatDirection == "Output");
        var saleReturns = rows.Where(row =>
            row.SourceType is "SaleRefund" or "SaleVoid" && row.VatDirection == "Output");
        var otherIncome = rows.Where(row =>
            row.SourceType == "OtherIncome" && row.VatDirection == "Output");
        var purchases = rows.Where(row =>
            row.SourceType == "Purchase" && row.VatDirection == "Input");
        var expenses = rows.Where(row =>
            row.SourceType == "Expense" && row.VatDirection == "Input");
        var outputVatFromSales = sales.Sum(row => row.VatAmount);
        var salesReturnVatAdjustments = saleReturns.Sum(row => row.VatAmount);
        var otherIncomeVat = otherIncome.Sum(row => row.VatAmount);
        var purchaseInputVat = purchases.Sum(row => row.VatAmount);
        var expenseInputVat = expenses.Sum(row => row.VatAmount);
        var totalOutputVat = outputVatFromSales + otherIncomeVat + salesReturnVatAdjustments;
        var totalInputVat = purchaseInputVat + expenseInputVat;
        var netVatPosition = totalOutputVat - totalInputVat;
        var breakdown = rows
            .GroupBy(row => new { row.TaxCode, row.VatRate })
            .Select(group => new VatRateBreakdownReadModel(
                group.Key.TaxCode,
                group.Key.VatRate,
                group.Sum(row => row.TaxableAmount),
                group.Where(row => row.VatDirection == "Output").Sum(row => row.VatAmount),
                group.Where(row => row.VatDirection == "Input").Sum(row => row.VatAmount)))
            .OrderBy(row => row.TaxCode)
            .ThenBy(row => row.VatRate)
            .ToArray();

        return new VatSummaryReadModel(
            filter.DateFrom.Date,
            filter.DateTo.Date,
            sales.Sum(row => row.TaxableAmount),
            outputVatFromSales,
            salesReturnVatAdjustments,
            otherIncome.Sum(row => row.TaxableAmount),
            otherIncomeVat,
            totalOutputVat,
            purchases.Sum(row => row.TaxableAmount),
            purchaseInputVat,
            expenses.Sum(row => row.TaxableAmount),
            expenseInputVat,
            totalInputVat,
            netVatPosition,
            netVatPosition > 0m ? "payable" : netVatPosition < 0m ? "receivable" : "none",
            exceptions.Count,
            breakdown);
    }

    public async Task<VatReconciliationReadModel> GetReconciliationAsync(
        VatReportFilter filter,
        long businessId,
        CancellationToken cancellationToken)
    {
        var summary = await GetSummaryAsync(filter, businessId, cancellationToken);
        var configuration = await dbContext.AccountingConfigurations
            .AsNoTracking()
            .SingleAsync(
                row => row.BusinessId == businessId,
                cancellationToken);
        var payableAccountId = configuration.VatPayableAccountId;
        var receivableAccountId = configuration.VatReceivableAccountId;
        if (!payableAccountId.HasValue)
            throw new InvalidOperationException($"{VatPayableSlot} account mapping is required.");
        if (!receivableAccountId.HasValue)
            throw new InvalidOperationException($"{VatReceivableSlot} account mapping is required.");

        _ = await dbContext.ChartAccounts
            .AsNoTracking()
            .SingleAsync(account => account.Id == payableAccountId.Value, cancellationToken);
        _ = await dbContext.ChartAccounts
            .AsNoTracking()
            .SingleAsync(account => account.Id == receivableAccountId.Value, cancellationToken);

        var lineQuery = dbContext.JournalEntryLines
            .AsNoTracking()
            .Where(line =>
                line.JournalEntry.BusinessId == businessId &&
                line.JournalEntry.Status == JournalEntryStatuses.Posted &&
                line.JournalEntry.PostingDate.Date >= filter.DateFrom.Date &&
                line.JournalEntry.PostingDate.Date <= filter.DateTo.Date);
        if (filter.OutletId.HasValue)
            lineQuery = lineQuery.Where(line => line.JournalEntry.OutletId == filter.OutletId.Value);
        if (filter.WarehouseId.HasValue)
            lineQuery = lineQuery.Where(line => line.JournalEntry.WarehouseId == filter.WarehouseId.Value);

        var lines = await lineQuery.ToListAsync(cancellationToken);
        var glOutput = lines
            .Where(line => line.AccountId == payableAccountId.Value)
            .Sum(line => line.CreditAmount - line.DebitAmount);
        var glInput = lines
            .Where(line => line.AccountId == receivableAccountId.Value)
            .Sum(line => line.DebitAmount - line.CreditAmount);
        var outputDifference = summary.TotalOutputVat - glOutput;
        var inputDifference = summary.TotalInputVat - glInput;
        var glNetPosition = glOutput - glInput;
        var netDifference = summary.NetVatPosition - glNetPosition;

        return new VatReconciliationReadModel(
            summary.TotalOutputVat,
            glOutput,
            outputDifference,
            summary.TotalOutputVat == glOutput ? "reconciled" : "difference",
            summary.TotalInputVat,
            glInput,
            inputDifference,
            summary.TotalInputVat == glInput ? "reconciled" : "difference",
            summary.NetVatPosition,
            glNetPosition,
            netDifference);
    }

    public async Task<IReadOnlyList<VatExceptionReadModel>> GetExceptionsAsync(
        VatReportFilter filter,
        long businessId,
        CancellationToken cancellationToken)
    {
        var outletIds = (await dbContext.Outlets
                .AsNoTracking()
                .Where(outlet => outlet.BusinessId == businessId)
                .Select(outlet => outlet.Id)
                .ToListAsync(cancellationToken))
            .ToHashSet();
        var sales = await dbContext.Sales
            .AsNoTracking()
            .Where(sale =>
                sale.SaleDate.Date >= filter.DateFrom.Date &&
                sale.SaleDate.Date <= filter.DateTo.Date &&
                sale.Tax > 0m &&
                (!filter.OutletId.HasValue || sale.OutletId == filter.OutletId.Value))
            .Include(sale => sale.Items)
            .ToListAsync(cancellationToken);

        return sales
            .Where(sale => outletIds.Contains(sale.OutletId))
            .Where(sale => sale.Items.Any(item =>
                !item.VatAmount.HasValue ||
                !item.TaxableAmount.HasValue ||
                !item.VatRate.HasValue))
            .Select(sale => new VatExceptionReadModel(
                sale.SaleDate,
                "Sale",
                sale.SaleNumber,
                null,
                null,
                "Incomplete VAT snapshot",
                sale.Tax))
            .ToArray();
    }

    private async Task<List<VatTransactionReadModel>> BuildEventsAsync(
        VatReportFilter filter,
        long businessId,
        CancellationToken cancellationToken)
    {
        var journalRows = await dbContext.JournalEntries
            .AsNoTracking()
            .Where(entry =>
                entry.BusinessId == businessId &&
                entry.Status == JournalEntryStatuses.Posted)
            .ToListAsync(cancellationToken);
        var journals = journalRows
            .Where(entry => entry.ReversalOfJournalEntryId == null)
            .GroupBy(entry => (entry.ReferenceType, entry.ReferenceId))
            .ToDictionary(group => group.Key, group => group.First());
        var result = new List<VatTransactionReadModel>();
        var reversalSaleIds = journalRows
            .Where(entry => entry.ReversalOfJournalEntryId.HasValue && entry.ReferenceType == "Sale")
            .Select(entry => entry.ReferenceId)
            .ToHashSet();
        var outletIds = (await dbContext.Outlets
                .AsNoTracking()
                .Where(outlet => outlet.BusinessId == businessId)
                .Select(outlet => outlet.Id)
                .ToListAsync(cancellationToken))
            .ToHashSet();
        var sales = (await dbContext.Sales
                .AsNoTracking()
                .Where(sale => !filter.OutletId.HasValue || sale.OutletId == filter.OutletId.Value)
                .Include(sale => sale.Items)
                .ToListAsync(cancellationToken))
            .Where(sale => outletIds.Contains(sale.OutletId))
            .Where(sale =>
                (sale.SaleDate.Date >= filter.DateFrom.Date && sale.SaleDate.Date <= filter.DateTo.Date) ||
                reversalSaleIds.Contains(sale.Id.ToString()))
            .ToList();

        foreach (var sale in sales)
        {
            foreach (var item in sale.Items.Where(item =>
                         item.VatAmount.HasValue && item.VatAmount.Value != 0m))
            {
                var vat = item.VatAmount ?? 0m;
                journals.TryGetValue(("Sale", sale.Id.ToString()), out var journal);
                result.Add(new VatTransactionReadModel(
                    journal?.PostingDate ?? sale.SaleDate,
                    "Sale",
                    sale.SaleNumber,
                    journal?.Id,
                    journal?.JournalNumber,
                    sale.OutletId,
                    null,
                    item.TaxCode ?? "",
                    item.VatRate,
                    item.TaxableAmount ?? 0m,
                    vat,
                    "Output",
                    journal?.ReversalOfJournalEntryId,
                    null));
            }
        }

        foreach (var sale in sales)
        {
            if (!journals.TryGetValue(("Sale", sale.Id.ToString()), out var original))
                continue;

            foreach (var reversal in journalRows.Where(entry =>
                         entry.ReversalOfJournalEntryId == original.Id))
            {
                var vat = sale.Tax;
                if (vat == 0m)
                    continue;
                // A reversal mirrors the original per-line snapshots, so mixed-rate sales are reported per rate.
                foreach (var item in sale.Items.Where(item => item.VatAmount.HasValue && item.VatAmount.Value != 0m))
                {
                    result.Add(new VatTransactionReadModel(
                        reversal.PostingDate,
                        reversal.JournalType == "JournalReversal" ? "SaleRefund" : "SaleVoid",
                        sale.SaleNumber,
                        reversal.Id,
                        reversal.JournalNumber,
                        sale.OutletId,
                        null,
                        item.TaxCode ?? "VAT",
                        item.VatRate,
                        -(item.TaxableAmount ?? 0m),
                        -(item.VatAmount ?? 0m),
                        "Output",
                        original.Id,
                        null));
                }
            }
        }

        var incomes = await dbContext.OtherIncomeEntries
            .AsNoTracking()
            .Where(income =>
                income.BusinessId == businessId &&
                income.IncomeDate.Date >= filter.DateFrom.Date &&
                income.IncomeDate.Date <= filter.DateTo.Date &&
                (!filter.OutletId.HasValue || income.OutletId == filter.OutletId.Value) &&
                income.VatAmount.HasValue &&
                income.VatAmount > 0m)
            .ToListAsync(cancellationToken);
        foreach (var income in incomes)
        {
            var vat = income.VatAmount ?? 0m;
            journals.TryGetValue(("OtherIncome", income.Id.ToString()), out var journal);
            result.Add(new VatTransactionReadModel(
                journal?.PostingDate ?? income.IncomeDate,
                "OtherIncome",
                income.ReferenceNumber ?? income.Id.ToString(),
                journal?.Id,
                journal?.JournalNumber,
                income.OutletId,
                null,
                income.TaxCode ?? "",
                income.VatRate,
                income.TaxableAmount ?? 0m,
                vat,
                "Output",
                null,
                null));
        }

        var expenses = await dbContext.Expenses
            .AsNoTracking()
            .Where(expense =>
                expense.BusinessId == businessId &&
                expense.ExpenseDate.Date >= filter.DateFrom.Date &&
                expense.ExpenseDate.Date <= filter.DateTo.Date &&
                (!filter.OutletId.HasValue || expense.OutletId == filter.OutletId.Value) &&
                expense.VatAmount.HasValue &&
                expense.VatAmount > 0m)
            .ToListAsync(cancellationToken);
        foreach (var expense in expenses)
        {
            var vat = expense.VatAmount ?? 0m;
            journals.TryGetValue(("Expense", expense.Id.ToString()), out var journal);
            result.Add(new VatTransactionReadModel(
                journal?.PostingDate ?? expense.ExpenseDate,
                "Expense",
                expense.ReferenceNumber ?? expense.Id.ToString(),
                journal?.Id,
                journal?.JournalNumber,
                expense.OutletId,
                null,
                expense.TaxCode ?? "",
                expense.VatRate,
                expense.TaxableAmount ?? 0m,
                vat,
                "Input",
                null,
                null));
        }

        var grns = await dbContext.Grns
            .AsNoTracking()
            .WhereBusiness(businessId)
            .Where(grn =>
                grn.ReceivedDate.Date >= filter.DateFrom.Date &&
                grn.ReceivedDate.Date <= filter.DateTo.Date &&
                (!filter.WarehouseId.HasValue ||
                    grn.PurchaseOrder.WarehouseId == filter.WarehouseId.Value) &&
                (!filter.OutletId.HasValue ||
                    grn.PurchaseOrder.OutletId == null ||
                    grn.PurchaseOrder.OutletId == filter.OutletId.Value))
            .Include(grn => grn.PurchaseOrder)
            .Include(grn => grn.Items)
            .ThenInclude(item => item.PurchaseOrderItem)
            .ToListAsync(cancellationToken);
        foreach (var grn in grns)
        {
            foreach (var item in grn.Items.Where(item =>
                         item.VatAmount.HasValue && item.VatAmount.Value != 0m))
            {
                var vat = item.VatAmount ?? 0m;
                journals.TryGetValue(("Grn", grn.Id.ToString()), out var journal);
                result.Add(new VatTransactionReadModel(
                    journal?.PostingDate ?? grn.ReceivedDate,
                    "Purchase",
                    grn.SupplierInvoiceNumber ?? $"GRN-{grn.Id}",
                    journal?.Id,
                    journal?.JournalNumber,
                    grn.PurchaseOrder.OutletId,
                    grn.PurchaseOrder.WarehouseId,
                    item.TaxCode ?? "",
                    item.VatRate,
                    item.TaxableAmount ?? 0m,
                    vat,
                    "Input",
                    null,
                    null));
            }
        }

        return result
            .Where(transaction =>
                string.IsNullOrWhiteSpace(filter.SourceType) ||
                transaction.SourceType.Equals(filter.SourceType, StringComparison.OrdinalIgnoreCase))
            .Where(transaction =>
                string.IsNullOrWhiteSpace(filter.SourceReference) ||
                transaction.SourceReference.Contains(
                    filter.SourceReference,
                    StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}
