namespace RetailPOS.Application.Sales.Abstractions;

public interface ISaleReceiptHistoryWriter
{
    Task RecordAsync(
        long saleId,
        long? terminalId,
        long? printedByUserId,
        string actionType,
        DateTime printedAtUtc,
        CancellationToken cancellationToken);
}
