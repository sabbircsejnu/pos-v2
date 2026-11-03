using RetailPOS.Application.ApReports.Abstractions;
using RetailPOS.Application.ApReports.ReadModels;
using RetailPOS.API.DTOs.Reports;

namespace RetailPOS.API.Services;

public sealed class ApReportTenantScope(ITenantAccessService tenantAccess)
    : IApReportTenantScope
{
    public Task<long> GetBusinessIdAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(tenantAccess.RequireBusinessId());
    }
}

public static class ApReportReadModelMapper
{
    public static ApPayablesReportFilter ToApplicationFilter(ApReportFilterDto filter) =>
        new(
            filter.AsOfDate,
            filter.DateFrom,
            filter.DateTo,
            filter.SupplierId,
            filter.OutletId,
            filter.WarehouseId,
            filter.Status,
            filter.Disputed,
            filter.AgingBucket,
            filter.SourceReference);

    public static ApSubledgerReportDto ToApi(ApSubledgerReadModel report) =>
        new()
        {
            AsOfDate = report.AsOfDate,
            TotalOutstanding = report.TotalOutstanding,
            Rows = report.Rows.Select(ToApi).ToList(),
            SupplierTotals = report.SupplierTotals.Select(ToApi).ToList(),
            Exceptions = ToApi(report.Exceptions)
        };

    public static ApAgingReportDto ToApi(ApAgingReadModel report) =>
        new()
        {
            AsOfDate = report.AsOfDate,
            TotalOutstanding = report.TotalOutstanding,
            CurrentNotDue = report.CurrentNotDue,
            Overdue1To30 = report.Overdue1To30,
            Overdue31To60 = report.Overdue31To60,
            Overdue61To90 = report.Overdue61To90,
            Overdue90Plus = report.Overdue90Plus,
            DisputedAmount = report.DisputedAmount,
            SupplierTotals = report.SupplierTotals.Select(ToApi).ToList(),
            Rows = report.Rows.Select(ToApi).ToList(),
            Exceptions = ToApi(report.Exceptions)
        };

    public static SupplierStatementReportDto ToApi(SupplierStatementReadModel report) =>
        new()
        {
            SupplierId = report.SupplierId,
            SupplierName = report.SupplierName,
            DateFrom = report.DateFrom,
            DateTo = report.DateTo,
            OpeningBalance = report.OpeningBalance,
            NewPayables = report.NewPayables,
            SupplierPayments = report.SupplierPayments,
            SupplierAdvanceApplications = report.SupplierAdvanceApplications,
            Reversals = report.Reversals,
            ClosingBalance = report.ClosingBalance,
            Rows = report.Rows.Select(row => new SupplierStatementRowDto
            {
                Date = row.Date,
                EventType = row.EventType,
                PayableDocumentId = row.PayableDocumentId,
                SourceReference = row.SourceReference,
                JournalReference = row.JournalReference,
                Debit = row.Debit,
                Credit = row.Credit,
                RunningBalance = row.RunningBalance
            }).ToList()
        };

    public static ApReconciliationReportDto ToApi(ApReconciliationReadModel report) =>
        new()
        {
            AsOfDate = report.AsOfDate,
            AccountId = report.AccountId,
            AccountSlot = report.AccountSlot,
            SubledgerTotal = report.SubledgerTotal,
            GeneralLedgerBalance = report.GeneralLedgerBalance,
            ReconciliationDifference = report.ReconciliationDifference,
            UnreconciledDetails = report.UnreconciledDetails.ToList()
        };

    private static ApPayableRowDto ToApi(ApPayableReadModel row) =>
        new()
        {
            PayableDocumentId = row.PayableDocumentId,
            SupplierId = row.SupplierId,
            SupplierName = row.SupplierName,
            SourceDocumentType = row.SourceDocumentType,
            SourceDocumentId = row.SourceDocumentId,
            SourceReference = row.SourceReference,
            RecognitionDate = row.RecognitionDate,
            SupplierInvoiceNumber = row.SupplierInvoiceNumber,
            SupplierInvoiceDate = row.SupplierInvoiceDate,
            DueDate = row.DueDate,
            OriginalPayableAmount = row.OriginalPayableAmount,
            EffectivePaymentAllocations = row.EffectivePaymentAllocations,
            EffectiveAdvanceUtilisations = row.EffectiveAdvanceUtilisations,
            ReversedPaymentAllocations = row.ReversedPaymentAllocations,
            ReversedAdvanceUtilisations = row.ReversedAdvanceUtilisations,
            OutstandingAmount = row.OutstandingAmount,
            Status = row.Status,
            IsDisputed = row.IsDisputed,
            DisputeReason = row.DisputeReason,
            WarehouseId = row.WarehouseId,
            SourceOutletId = row.SourceOutletId,
            SourceJournalEntryId = row.SourceJournalEntryId,
            SourceJournalNumber = row.SourceJournalNumber
        };

    private static ApSupplierTotalDto ToApi(ApSupplierTotalReadModel total) =>
        new()
        {
            SupplierId = total.SupplierId,
            SupplierName = total.SupplierName,
            TotalOutstanding = total.TotalOutstanding,
            DisputedAmount = total.DisputedAmount
        };

    private static ApExceptionSummaryDto ToApi(ApExceptionSummaryReadModel summary) =>
        new()
        {
            LegacyUnreconciledCount = summary.LegacyUnreconciledCount,
            SupplierOwnershipExceptionCount = summary.SupplierOwnershipExceptionCount,
            Details = summary.Details.ToList()
        };
}
