using RetailPOS.Application.Settings.Abstractions;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.ApReports.Abstractions;
using RetailPOS.Application.ApReports.ReadModels;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.ApReports;

public sealed class ApPayablesReportReadStore(
    RetailPOSDbContext dbContext,
    ICompanyTimeZoneProvider timeZones)
    : IApPayablesReportReadStore
{
    public async Task<ApReconciliationReadModel> GetPayableReconciliationAsync(
        ApPayablesReportFilter filter,
        long businessId,
        CancellationToken cancellationToken)
    {
        var asOf = (filter.AsOfDate ?? await timeZones.GetTodayAsync(businessId, cancellationToken)).Date;
        var rows = await LoadRowsAsync(filter, businessId, asOf, cancellationToken);
        var account = await ResolveAccountAsync(
            "supplier_payable",
            businessId,
            cancellationToken);
        var generalLedger = await GlBalanceAsync(
            account.Id,
            filter,
            businessId,
            asOf,
            cancellationToken);
        var details = await ExceptionsAsync(cancellationToken);
        var subledger = rows.Sum(row => row.OutstandingAmount);

        return new ApReconciliationReadModel(
            asOf,
            account.Id,
            "supplier_payable",
            subledger,
            generalLedger,
            subledger - generalLedger,
            details.Details);
    }

    public async Task<ApReconciliationReadModel> GetAdvanceReconciliationAsync(
        ApPayablesReportFilter filter,
        long businessId,
        CancellationToken cancellationToken)
    {
        var asOf = (filter.AsOfDate ?? await timeZones.GetTodayAsync(businessId, cancellationToken)).Date;
        var advances = await dbContext.SupplierAdvances
            .AsNoTracking()
            .Where(advance => advance.BusinessId == businessId
                && advance.PostingDate.HasValue
                && advance.PostingDate.Value.Date <= asOf)
            .ToListAsync(cancellationToken);
        var utilisations = await dbContext.SupplierAdvanceUtilizations
            .AsNoTracking()
            .Where(utilisation => utilisation.BusinessId == businessId
                && utilisation.EffectiveDate.Date <= asOf)
            .ToListAsync(cancellationToken);
        var reversalJournalEntryIds = await dbContext.JournalEntries
            .AsNoTracking()
            .Where(journal => journal.PostingDate.Date <= asOf)
            .Select(journal => journal.ReversalOfJournalEntryId)
            .ToListAsync(cancellationToken);
        var activeAdvances = advances
            .Where(advance => advance.Status != "reversed"
                || !reversalJournalEntryIds.Contains(advance.JournalEntryId))
            .ToList();
        var unapplied = activeAdvances.Sum(advance => advance.Amount)
            - utilisations
                .Where(utilisation => utilisation.Status != "reversal"
                    && !HasUtilizationReversalAsOf(utilisation, utilisations, asOf))
                .Sum(utilisation => utilisation.Amount);
        var account = await ResolveAccountAsync(
            "supplier_advance",
            businessId,
            cancellationToken);
        var generalLedger = await GlBalanceAsync(
            account.Id,
            filter,
            businessId,
            asOf,
            cancellationToken);
        var details = await ExceptionsAsync(cancellationToken);

        return new ApReconciliationReadModel(
            asOf,
            account.Id,
            "supplier_advance",
            unapplied,
            generalLedger,
            unapplied - generalLedger,
            details.Details);
    }

    public async Task<SupplierStatementReadModel> GetSupplierStatementAsync(
        ApPayablesReportFilter filter,
        long businessId,
        CancellationToken cancellationToken)
    {
        if (!filter.SupplierId.HasValue)
            throw new InvalidOperationException("SupplierId is required for a supplier statement.");

        var dateFrom = (filter.DateFrom ?? DateTime.MinValue).Date;
        var dateTo = (filter.DateTo ?? await timeZones.GetTodayAsync(businessId, cancellationToken)).Date;
        var payableRows = await LoadRowsAsync(filter, businessId, dateTo, cancellationToken);
        var supplier = await dbContext.Suppliers
            .AsNoTracking()
            .SingleAsync(
                supplier => supplier.Id == filter.SupplierId.Value,
                cancellationToken);
        var events = new List<StatementEvent>();
        foreach (var row in payableRows)
        {
            events.Add(new StatementEvent(
                row.RecognitionDate,
                "New payable",
                row.PayableDocumentId,
                row.SourceReference,
                row.SourceJournalNumber,
                0m,
                row.OriginalPayableAmount));
            var allocations = await dbContext.SupplierPaymentAllocations
                .AsNoTracking()
                .Where(allocation => allocation.SupplierPayableDocumentId == row.PayableDocumentId
                    && allocation.Status != "pending")
                .ToListAsync(cancellationToken);
            foreach (var allocation in allocations)
            {
                if (allocation.EffectiveDate.HasValue
                    && allocation.EffectiveDate.Value.Date <= dateTo)
                    events.Add(new StatementEvent(
                        allocation.EffectiveDate.Value.Date,
                        "Supplier payment",
                        row.PayableDocumentId,
                        row.SourceReference,
                        allocation.JournalEntryId?.ToString(),
                        allocation.AllocatedAmount,
                        0m));
                if (allocation.ReversedEffectiveDate.HasValue
                    && allocation.ReversedEffectiveDate.Value.Date <= dateTo)
                    events.Add(new StatementEvent(
                        allocation.ReversedEffectiveDate.Value.Date,
                        "Payment reversal",
                        row.PayableDocumentId,
                        row.SourceReference,
                        allocation.ReversalJournalEntryId?.ToString(),
                        0m,
                        allocation.AllocatedAmount));
            }

            var utilisations = await dbContext.SupplierAdvanceUtilizations
                .AsNoTracking()
                .Where(utilisation => utilisation.SupplierPayableDocumentId == row.PayableDocumentId)
                .ToListAsync(cancellationToken);
            foreach (var utilisation in utilisations)
            {
                if (utilisation.Status == "reversal"
                    && utilisation.ReversalOfUtilizationId.HasValue)
                {
                    if (utilisation.EffectiveDate.Date <= dateTo)
                        events.Add(new StatementEvent(
                            utilisation.EffectiveDate.Date,
                            "Advance utilisation reversal",
                            row.PayableDocumentId,
                            row.SourceReference,
                            utilisation.JournalEntryId.ToString(),
                            0m,
                            utilisation.Amount));
                }
                else if (utilisation.EffectiveDate.Date <= dateTo)
                {
                    events.Add(new StatementEvent(
                        utilisation.EffectiveDate.Date,
                        "Supplier advance application",
                        row.PayableDocumentId,
                        row.SourceReference,
                        utilisation.JournalEntryId.ToString(),
                        utilisation.Amount,
                        0m));
                }
            }
        }

        var orderedEvents = events
            .Where(statementEvent => statementEvent.Date <= dateTo)
            .OrderBy(statementEvent => statementEvent.Date)
            .ThenBy(statementEvent => statementEvent.PayableDocumentId)
            .ThenBy(statementEvent => statementEvent.EventType)
            .ToList();
        var opening = orderedEvents
            .Where(statementEvent => statementEvent.Date < dateFrom)
            .Sum(statementEvent => statementEvent.Credit - statementEvent.Debit);
        var balance = opening;
        var statementRows = new List<SupplierStatementRowReadModel>();
        decimal newPayables = 0m;
        decimal supplierPayments = 0m;
        decimal supplierAdvanceApplications = 0m;
        decimal reversals = 0m;
        foreach (var statementEvent in orderedEvents.Where(statementEvent => statementEvent.Date >= dateFrom))
        {
            balance += statementEvent.Credit - statementEvent.Debit;
            statementRows.Add(new SupplierStatementRowReadModel(
                statementEvent.Date,
                statementEvent.EventType,
                statementEvent.PayableDocumentId,
                statementEvent.SourceReference,
                statementEvent.JournalReference,
                statementEvent.Debit,
                statementEvent.Credit,
                balance));
            if (statementEvent.EventType == "New payable")
                newPayables += statementEvent.Credit;
            else if (statementEvent.EventType == "Supplier payment")
                supplierPayments += statementEvent.Debit;
            else if (statementEvent.EventType == "Supplier advance application")
                supplierAdvanceApplications += statementEvent.Debit;
            else
                reversals += statementEvent.Credit - statementEvent.Debit;
        }

        return new SupplierStatementReadModel(
            supplier.Id,
            supplier.Name,
            dateFrom,
            dateTo,
            opening,
            newPayables,
            supplierPayments,
            supplierAdvanceApplications,
            reversals,
            balance,
            statementRows);
    }

    public async Task<ApSubledgerReadModel> GetSubledgerAsync(
        ApPayablesReportFilter filter,
        long businessId,
        CancellationToken cancellationToken)
    {
        var asOf = (filter.AsOfDate ?? await timeZones.GetTodayAsync(businessId, cancellationToken)).Date;
        var rows = ApplyStatusFilter(
            await LoadRowsAsync(filter, businessId, asOf, cancellationToken),
            filter.Status);
        return new ApSubledgerReadModel(
            asOf,
            rows.Sum(row => row.OutstandingAmount),
            rows,
            SupplierTotals(rows),
            await ExceptionsAsync(cancellationToken));
    }

    public async Task<ApAgingReadModel> GetAgingAsync(
        ApPayablesReportFilter filter,
        long businessId,
        CancellationToken cancellationToken)
    {
        var asOf = (filter.AsOfDate ?? await timeZones.GetTodayAsync(businessId, cancellationToken)).Date;
        var rows = ApplyStatusFilter(
            await LoadRowsAsync(filter, businessId, asOf, cancellationToken),
            filter.Status);
        if (!string.IsNullOrWhiteSpace(filter.AgingBucket))
            rows = rows
                .Where(row => Bucket(row.DueDate, asOf)
                    .Equals(filter.AgingBucket, StringComparison.OrdinalIgnoreCase))
                .ToList();

        decimal totalOutstanding = 0m;
        decimal currentNotDue = 0m;
        decimal overdue1To30 = 0m;
        decimal overdue31To60 = 0m;
        decimal overdue61To90 = 0m;
        decimal overdue90Plus = 0m;
        decimal disputedAmount = 0m;
        foreach (var row in rows)
        {
            totalOutstanding += row.OutstandingAmount;
            if (row.IsDisputed)
                disputedAmount += row.OutstandingAmount;

            switch (Bucket(row.DueDate, asOf))
            {
                case "Current / Not Due":
                    currentNotDue += row.OutstandingAmount;
                    break;
                case "1–30 days overdue":
                    overdue1To30 += row.OutstandingAmount;
                    break;
                case "31–60 days overdue":
                    overdue31To60 += row.OutstandingAmount;
                    break;
                case "61–90 days overdue":
                    overdue61To90 += row.OutstandingAmount;
                    break;
                default:
                    overdue90Plus += row.OutstandingAmount;
                    break;
            }
        }

        return new ApAgingReadModel(
            asOf,
            totalOutstanding,
            currentNotDue,
            overdue1To30,
            overdue31To60,
            overdue61To90,
            overdue90Plus,
            disputedAmount,
            SupplierTotals(rows),
            rows,
            await ExceptionsAsync(cancellationToken));
    }

    private async Task<List<ApPayableReadModel>> LoadRowsAsync(
        ApPayablesReportFilter filter,
        long businessId,
        DateTime asOf,
        CancellationToken cancellationToken)
    {
        var documents = await dbContext.SupplierPayableDocuments
            .AsNoTracking()
            .Include(document => document.Supplier)
            .Where(document => document.BusinessId == businessId
                && document.RecognitionDate.Date <= asOf)
            .OrderBy(document => document.DueDate)
            .ThenBy(document => document.Id)
            .ToListAsync(cancellationToken);
        var sourceJournalIds = documents
            .Select(document => document.SourceJournalEntryId)
            .Distinct()
            .ToArray();
        var sourceJournals = (await dbContext.JournalEntries
                .AsNoTracking()
                .ToListAsync(cancellationToken))
            .Where(journal => sourceJournalIds.Contains(journal.Id))
            .ToDictionary(journal => journal.Id);

        if (filter.SupplierId.HasValue)
            documents = documents
                .Where(document => document.SupplierId == filter.SupplierId.Value)
                .ToList();
        if (filter.WarehouseId.HasValue)
            documents = documents
                .Where(document => document.WarehouseId == filter.WarehouseId.Value)
                .ToList();
        if (filter.OutletId.HasValue)
            documents = documents
                .Where(document => sourceJournals.TryGetValue(document.SourceJournalEntryId, out var journal)
                    && journal.OutletId == filter.OutletId.Value)
                .ToList();
        if (!string.IsNullOrWhiteSpace(filter.SourceReference))
            documents = documents
                .Where(document => document.SourceReference.Contains(
                    filter.SourceReference,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();

        var documentIds = documents.Select(document => document.Id).ToHashSet();
        var allocations = (await dbContext.SupplierPaymentAllocations
                .AsNoTracking()
                .ToListAsync(cancellationToken))
            .Where(allocation => allocation.SupplierPayableDocumentId.HasValue
                && documentIds.Contains(allocation.SupplierPayableDocumentId.Value))
            .ToList();
        var utilisations = (await dbContext.SupplierAdvanceUtilizations
                .AsNoTracking()
                .ToListAsync(cancellationToken))
            .Where(utilisation => documentIds.Contains(utilisation.SupplierPayableDocumentId))
            .ToList();

        return documents.Select(document =>
            {
                var documentAllocations = allocations
                    .Where(allocation => allocation.SupplierPayableDocumentId == document.Id
                        && allocation.EffectiveDate.HasValue
                        && allocation.EffectiveDate.Value.Date <= asOf)
                    .ToList();
                var documentUtilisations = utilisations
                    .Where(utilisation => utilisation.SupplierPayableDocumentId == document.Id)
                    .ToList();
                var activeAllocations = documentAllocations
                    .Where(allocation => !allocation.ReversedEffectiveDate.HasValue
                        || allocation.ReversedEffectiveDate.Value.Date > asOf)
                    .ToList();
                var activeUtilisations = documentUtilisations
                    .Where(utilisation => utilisation.Status != "reversal"
                        && utilisation.EffectiveDate.Date <= asOf
                        && !HasUtilizationReversalAsOf(utilisation, documentUtilisations, asOf))
                    .ToList();
                var reversedAllocations = documentAllocations
                    .Where(allocation => allocation.ReversedEffectiveDate.HasValue
                        && allocation.ReversedEffectiveDate.Value.Date <= asOf)
                    .ToList();
                var reversedUtilisations = documentUtilisations
                    .Where(utilisation => utilisation.Status == "reversal"
                        && utilisation.EffectiveDate.Date <= asOf)
                    .ToList();
                var paymentTotal = activeAllocations.Sum(allocation => allocation.AllocatedAmount);
                var advanceTotal = activeUtilisations.Sum(utilisation => utilisation.Amount);
                var outstanding = Math.Max(
                    0m,
                    document.OriginalPayableAmount - paymentTotal - advanceTotal);
                sourceJournals.TryGetValue(document.SourceJournalEntryId, out var sourceJournal);

                return new ApPayableReadModel(
                    document.Id,
                    document.SupplierId,
                    document.Supplier.Name,
                    document.SourceDocumentType,
                    document.SourceDocumentId,
                    document.SourceReference,
                    document.RecognitionDate,
                    document.SupplierInvoiceNumber,
                    document.SupplierInvoiceDate,
                    document.DueDate,
                    document.OriginalPayableAmount,
                    paymentTotal,
                    advanceTotal,
                    reversedAllocations.Sum(allocation => allocation.AllocatedAmount),
                    reversedUtilisations.Sum(utilisation => utilisation.Amount),
                    outstanding,
                    outstanding == 0m
                        ? SupplierPayableDocumentStatuses.Paid
                        : outstanding < document.OriginalPayableAmount
                            ? SupplierPayableDocumentStatuses.Partial
                            : SupplierPayableDocumentStatuses.Open,
                    document.IsDisputed,
                    document.DisputeReason,
                    document.WarehouseId,
                    sourceJournal?.OutletId,
                    document.SourceJournalEntryId,
                    sourceJournal?.JournalNumber);
            })
            .Where(row => !filter.Disputed.HasValue || row.IsDisputed == filter.Disputed.Value)
            .ToList();
    }

    private async Task<ApExceptionSummaryReadModel> ExceptionsAsync(
        CancellationToken cancellationToken)
    {
        var legacyCount = await dbContext.Bills
            .AsNoTracking()
            .CountAsync(cancellationToken);
        var details = legacyCount == 0
            ? new List<string>()
            : [$"{legacyCount} legacy Bill records excluded from authoritative AP totals."];
        var ownershipExceptionCount = 0;
        try
        {
            ownershipExceptionCount = await dbContext.Database
                .SqlQueryRaw<int>("SELECT COUNT(*) AS \"Value\" FROM supplier_business_ownership_exceptions")
                .SingleAsync(cancellationToken);
            if (ownershipExceptionCount > 0)
                details.Add(
                    $"{ownershipExceptionCount} supplier ownership exceptions require resolution before authoritative AP attribution.");
        }
        catch
        {
            // Lightweight providers and pre-migration databases do not expose the exception table.
        }

        return new ApExceptionSummaryReadModel(
            legacyCount,
            ownershipExceptionCount,
            details);
    }

    private async Task<ChartAccount> ResolveAccountAsync(
        string slot,
        long businessId,
        CancellationToken cancellationToken)
    {
        var configuration = await dbContext.AccountingConfigurations
            .AsNoTracking()
            .SingleAsync(configuration => configuration.BusinessId == businessId, cancellationToken);
        var accountId = slot == "supplier_payable"
            ? configuration.SupplierPayableAccountId
            : configuration.SupplierAdvanceAccountId;
        if (!accountId.HasValue)
            throw new InvalidOperationException($"{slot} account mapping is required.");
        return await dbContext.ChartAccounts
            .AsNoTracking()
            .SingleAsync(
                account => account.Id == accountId.Value && account.BusinessId == businessId,
                cancellationToken);
    }

    private async Task<decimal> GlBalanceAsync(
        long accountId,
        ApPayablesReportFilter filter,
        long businessId,
        DateTime asOf,
        CancellationToken cancellationToken)
    {
        var query = dbContext.JournalEntryLines
            .AsNoTracking()
            .Where(line => line.AccountId == accountId
                && line.JournalEntry.BusinessId == businessId
                && line.JournalEntry.Status == JournalEntryStatuses.Posted
                && line.JournalEntry.PostingDate.Date <= asOf);
        if (filter.OutletId.HasValue)
            query = query.Where(line => line.JournalEntry.OutletId == filter.OutletId.Value);
        if (filter.WarehouseId.HasValue)
            query = query.Where(line => line.JournalEntry.WarehouseId == filter.WarehouseId.Value);
        return await query.SumAsync(
            line => line.CreditAmount - line.DebitAmount,
            cancellationToken);
    }

    private static List<ApPayableReadModel> ApplyStatusFilter(
        List<ApPayableReadModel> rows,
        string? status) =>
        string.IsNullOrWhiteSpace(status)
            ? rows
            : rows.Where(row => row.Status.Equals(status, StringComparison.OrdinalIgnoreCase)).ToList();

    private static List<ApSupplierTotalReadModel> SupplierTotals(
        IEnumerable<ApPayableReadModel> rows) =>
        rows.GroupBy(row => new { row.SupplierId, row.SupplierName })
            .Select(group => new ApSupplierTotalReadModel(
                group.Key.SupplierId,
                group.Key.SupplierName,
                group.Sum(row => row.OutstandingAmount),
                group.Where(row => row.IsDisputed).Sum(row => row.OutstandingAmount)))
            .OrderBy(total => total.SupplierName)
            .ToList();

    private static string Bucket(DateTime dueDate, DateTime asOf)
    {
        var days = (asOf.Date - dueDate.Date).Days;
        return days <= 0
            ? "Current / Not Due"
            : days <= 30
                ? "1–30 days overdue"
                : days <= 60
                    ? "31–60 days overdue"
                    : days <= 90
                        ? "61–90 days overdue"
                        : "90+ days overdue";
    }

    private static bool HasUtilizationReversalAsOf(
        SupplierAdvanceUtilization utilization,
        IEnumerable<SupplierAdvanceUtilization> all,
        DateTime asOf) =>
        all.Any(reversal => reversal.Status == "reversal"
            && reversal.ReversalOfUtilizationId == utilization.Id
            && reversal.EffectiveDate.Date <= asOf);

    private sealed record StatementEvent(
        DateTime Date,
        string EventType,
        long PayableDocumentId,
        string SourceReference,
        string? JournalReference,
        decimal Debit,
        decimal Credit);
}
