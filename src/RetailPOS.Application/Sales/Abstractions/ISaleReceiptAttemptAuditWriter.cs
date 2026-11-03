namespace RetailPOS.Application.Sales.Abstractions;

public sealed record SaleReceiptAttemptAuditRecord(
    string ActionType,
    string Summary,
    string Status,
    string? ErrorMessage,
    long SaleId,
    string SaleNumber,
    long OutletId,
    long? TerminalId,
    string? OutputMode,
    string Action,
    string? Message);

public interface ISaleReceiptAttemptAuditWriter
{
    Task RecordBestEffortAsync(
        SaleReceiptAttemptAuditRecord record,
        CancellationToken cancellationToken);
}
