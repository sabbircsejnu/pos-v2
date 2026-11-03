namespace RetailPOS.Application.VatReports.ReadModels;

public sealed record VatReportFilter(
    DateTime DateFrom,
    DateTime DateTo,
    long? OutletId = null,
    long? WarehouseId = null,
    string? VatDirection = null,
    string? SourceType = null,
    string? SourceReference = null,
    string? TaxCode = null,
    decimal? VatRate = null,
    long? LocationId = null,
    string? LocationType = null);

public sealed record VatTransactionReadModel(
    DateTime PostingDate,
    string SourceType,
    string SourceReference,
    long? JournalEntryId,
    string? JournalNumber,
    long? OutletId,
    long? WarehouseId,
    string TaxCode,
    decimal? VatRate,
    decimal TaxableAmount,
    decimal VatAmount,
    string VatDirection,
    long? OriginalJournalEntryId,
    string? ExceptionReason);

public sealed record VatRateBreakdownReadModel(
    string TaxCode,
    decimal? VatRate,
    decimal TaxableAmount,
    decimal OutputVat,
    decimal InputVat);

public sealed record VatSummaryReadModel(
    DateTime DateFrom,
    DateTime DateTo,
    decimal NetTaxableSales,
    decimal OutputVatFromSales,
    decimal SalesReturnVatAdjustments,
    decimal NetTaxableOtherIncome,
    decimal OtherIncomeOutputVat,
    decimal TotalOutputVat,
    decimal NetTaxablePurchases,
    decimal PurchaseInputVat,
    decimal NetTaxableExpenses,
    decimal ExpenseInputVat,
    decimal TotalInputVat,
    decimal NetVatPosition,
    string Position,
    int ExceptionCount,
    IReadOnlyList<VatRateBreakdownReadModel> Breakdown);

public sealed record VatReconciliationReadModel(
    decimal ReportOutputVat,
    decimal GlOutputVat,
    decimal OutputDifference,
    string OutputStatus,
    decimal ReportInputVat,
    decimal GlInputVat,
    decimal InputDifference,
    string InputStatus,
    decimal ReportNetVatPosition,
    decimal GlNetVatPosition,
    decimal OverallDifference);

public sealed record VatExceptionReadModel(
    DateTime? PostingDate,
    string SourceType,
    string SourceReference,
    long? JournalEntryId,
    string? JournalNumber,
    string Reason,
    decimal? KnownVatAmount);
