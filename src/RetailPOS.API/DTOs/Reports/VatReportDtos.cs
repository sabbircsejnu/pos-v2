namespace RetailPOS.API.DTOs.Reports;

public class VatReportFilterDto
{
    public DateTime DateFrom { get; set; }
    public DateTime DateTo { get; set; }
    public long? OutletId { get; set; }
    public long? WarehouseId { get; set; }
    public string? VatDirection { get; set; }
    public string? SourceType { get; set; }
    public string? SourceReference { get; set; }
    public string? TaxCode { get; set; }
    public decimal? VatRate { get; set; }
    public long? LocationId { get; set; }
    public string? LocationType { get; set; }
}

public class VatTransactionDto
{
    public DateTime PostingDate { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public string SourceReference { get; set; } = string.Empty;
    public long? JournalEntryId { get; set; }
    public string? JournalNumber { get; set; }
    public long? OutletId { get; set; }
    public long? WarehouseId { get; set; }
    public string TaxCode { get; set; } = string.Empty;
    public decimal? VatRate { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal VatAmount { get; set; }
    public string VatDirection { get; set; } = string.Empty;
    public long? OriginalJournalEntryId { get; set; }
    public string? ExceptionReason { get; set; }
}

public class VatSummaryReportDto
{
    public DateTime DateFrom { get; set; }
    public DateTime DateTo { get; set; }
    public decimal NetTaxableSales { get; set; }
    public decimal OutputVatFromSales { get; set; }
    public decimal SalesReturnVatAdjustments { get; set; }
    public decimal NetTaxableOtherIncome { get; set; }
    public decimal OtherIncomeOutputVat { get; set; }
    public decimal TotalOutputVat { get; set; }
    public decimal NetTaxablePurchases { get; set; }
    public decimal PurchaseInputVat { get; set; }
    public decimal NetTaxableExpenses { get; set; }
    public decimal ExpenseInputVat { get; set; }
    public decimal TotalInputVat { get; set; }
    public decimal NetVatPosition { get; set; }
    public string Position { get; set; } = "none";
    public int ExceptionCount { get; set; }
    public List<VatRateBreakdownDto> Breakdown { get; set; } = new();
}

public class VatRateBreakdownDto
{
    public string TaxCode { get; set; } = string.Empty;
    public decimal? VatRate { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal OutputVat { get; set; }
    public decimal InputVat { get; set; }
}

public class VatReconciliationReportDto
{
    public decimal ReportOutputVat { get; set; }
    public decimal GlOutputVat { get; set; }
    public decimal OutputDifference { get; set; }
    public string OutputStatus { get; set; } = string.Empty;
    public decimal ReportInputVat { get; set; }
    public decimal GlInputVat { get; set; }
    public decimal InputDifference { get; set; }
    public string InputStatus { get; set; } = string.Empty;
    public decimal ReportNetVatPosition { get; set; }
    public decimal GlNetVatPosition { get; set; }
    public decimal OverallDifference { get; set; }
}

public class VatExceptionDto
{
    public DateTime? PostingDate { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public string SourceReference { get; set; } = string.Empty;
    public long? JournalEntryId { get; set; }
    public string? JournalNumber { get; set; }
    public string Reason { get; set; } = string.Empty;
    public decimal? KnownVatAmount { get; set; }
}

public class VatExceptionReportDto
{
    public List<VatExceptionDto> Items { get; set; } = new();
}
