namespace RetailPOS.Application.AccountingReports.ReadModels;

public sealed record CogsReportFilter(
    DateTime? DateFrom,
    DateTime? DateTo,
    long? OutletId,
    long? WarehouseId,
    long? ProductId,
    long? VariantId);

public sealed record CogsReportReadModel(
    IReadOnlyList<CogsReportRowReadModel> Rows,
    decimal TotalCogs,
    decimal GeneralLedgerBalance,
    decimal ReconciliationDifference,
    long CogsAccountId);

public sealed record CogsReportRowReadModel(
    long JournalEntryId,
    string? JournalNumber,
    DateTime PostingDate,
    string JournalType,
    string ReferenceType,
    string ReferenceId,
    string? ReferenceNumber,
    long? OutletId,
    long? WarehouseId,
    decimal DebitAmount,
    decimal CreditAmount,
    decimal NetCogs,
    string? Description);
