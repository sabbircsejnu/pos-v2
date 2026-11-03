namespace RetailPOS.Application.Sales.Abstractions;

public sealed record SaleReceiptActionAuditRecord(
    string ActionType,
    string Summary,
    long SaleId,
    string SaleNumber,
    long OutletId,
    long? TerminalId,
    string OutputMode,
    string Action);

public interface ISaleReceiptActionAuditWriter
{
    Task RecordBestEffortAsync(
        SaleReceiptActionAuditRecord record,
        CancellationToken cancellationToken);
}
