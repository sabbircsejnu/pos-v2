using RetailPOS.Core.Entities;

namespace RetailPOS.API.DTOs.Reports;

public class ApReportFilterDto
{
    public DateTime? AsOfDate { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public long? SupplierId { get; set; }
    public long? OutletId { get; set; }
    public long? WarehouseId { get; set; }
    public long? LocationId { get; set; }
    public string? LocationType { get; set; }
    public string? Status { get; set; }
    public bool? Disputed { get; set; }
    public string? AgingBucket { get; set; }
    public string? SourceReference { get; set; }
}

public class ApPayableRowDto
{
    public long PayableDocumentId { get; set; }
    public long SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public string SourceDocumentType { get; set; } = string.Empty;
    public long SourceDocumentId { get; set; }
    public string SourceReference { get; set; } = string.Empty;
    public DateTime RecognitionDate { get; set; }
    public string? SupplierInvoiceNumber { get; set; }
    public DateTime? SupplierInvoiceDate { get; set; }
    public DateTime DueDate { get; set; }
    public decimal OriginalPayableAmount { get; set; }
    public decimal EffectivePaymentAllocations { get; set; }
    public decimal EffectiveAdvanceUtilisations { get; set; }
    public decimal ReversedPaymentAllocations { get; set; }
    public decimal ReversedAdvanceUtilisations { get; set; }
    public decimal OutstandingAmount { get; set; }
    public string Status { get; set; } = SupplierPayableDocumentStatuses.Open;
    public bool IsDisputed { get; set; }
    public string? DisputeReason { get; set; }
    public long? WarehouseId { get; set; }
    public long? SourceOutletId { get; set; }
    public long SourceJournalEntryId { get; set; }
    public string? SourceJournalNumber { get; set; }
}

public class ApSubledgerReportDto
{
    public DateTime AsOfDate { get; set; }
    public decimal TotalOutstanding { get; set; }
    public List<ApPayableRowDto> Rows { get; set; } = new();
    public List<ApSupplierTotalDto> SupplierTotals { get; set; } = new();
    public ApExceptionSummaryDto Exceptions { get; set; } = new();
}

public class ApAgingReportDto
{
    public DateTime AsOfDate { get; set; }
    public decimal TotalOutstanding { get; set; }
    public decimal CurrentNotDue { get; set; }
    public decimal Overdue1To30 { get; set; }
    public decimal Overdue31To60 { get; set; }
    public decimal Overdue61To90 { get; set; }
    public decimal Overdue90Plus { get; set; }
    public decimal DisputedAmount { get; set; }
    public List<ApSupplierTotalDto> SupplierTotals { get; set; } = new();
    public List<ApPayableRowDto> Rows { get; set; } = new();
    public ApExceptionSummaryDto Exceptions { get; set; } = new();
}

public class ApSupplierTotalDto
{
    public long SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public decimal TotalOutstanding { get; set; }
    public decimal DisputedAmount { get; set; }
}

public class ApExceptionSummaryDto
{
    public int LegacyUnreconciledCount { get; set; }
    public int SupplierOwnershipExceptionCount { get; set; }
    public List<string> Details { get; set; } = new();
}

public class SupplierStatementReportDto
{
    public long SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public DateTime DateFrom { get; set; }
    public DateTime DateTo { get; set; }
    public decimal OpeningBalance { get; set; }
    public decimal NewPayables { get; set; }
    public decimal SupplierPayments { get; set; }
    public decimal SupplierAdvanceApplications { get; set; }
    public decimal Reversals { get; set; }
    public decimal ClosingBalance { get; set; }
    public List<SupplierStatementRowDto> Rows { get; set; } = new();
}

public class SupplierStatementRowDto
{
    public DateTime Date { get; set; }
    public string EventType { get; set; } = string.Empty;
    public long PayableDocumentId { get; set; }
    public string SourceReference { get; set; } = string.Empty;
    public string? JournalReference { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public decimal RunningBalance { get; set; }
}

public class ApReconciliationReportDto
{
    public DateTime AsOfDate { get; set; }
    public long AccountId { get; set; }
    public string AccountSlot { get; set; } = string.Empty;
    public decimal SubledgerTotal { get; set; }
    public decimal GeneralLedgerBalance { get; set; }
    public decimal ReconciliationDifference { get; set; }
    public List<string> UnreconciledDetails { get; set; } = new();
}