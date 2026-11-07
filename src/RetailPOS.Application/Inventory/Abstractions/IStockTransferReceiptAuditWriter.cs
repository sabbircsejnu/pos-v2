namespace RetailPOS.Application.Inventory.Abstractions;

public sealed record StockTransferReceiptAuditLine(
    long VariantId,
    decimal DispatchedQuantity,
    decimal AcceptedQuantity,
    decimal RejectedQuantity,
    decimal MissingQuantity,
    decimal DamagedQuantity,
    string? Remarks);

public sealed record StockTransferReceiptAudit(
    long TransferId,
    string? TransferNo,
    string Status,
    long LocationId,
    string LocationType,
    long? ActorUserId,
    string? Reason,
    IReadOnlyList<StockTransferReceiptAuditLine> Lines);

/// <summary>
/// Records rejected, missing and damaged units. The caller invokes it inside the transaction that changes the
/// transfer, so a failure here rolls the whole receipt back.
/// </summary>
public interface IStockTransferReceiptAuditWriter
{
    Task RecordAsync(StockTransferReceiptAudit audit, CancellationToken cancellationToken);
}
