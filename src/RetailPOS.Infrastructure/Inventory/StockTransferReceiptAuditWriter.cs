using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Audit;

namespace RetailPOS.Infrastructure.StockTransfers;

public sealed class StockTransferReceiptAuditWriter(IAuditService auditService)
    : IStockTransferReceiptAuditWriter
{
    public Task RecordAsync(StockTransferReceiptAudit audit, CancellationToken cancellationToken) =>
        auditService.RecordAsync(
            new AuditEventInput
            {
                ActionType = AuditActionType.Adjustment,
                Module = AuditModule.Inventory,
                Summary = $"Stock transfer {audit.TransferNo ?? audit.TransferId.ToString()} received with rejected, missing or damaged units",
                PrimaryEntity = ("StockTransfer", audit.TransferId.ToString()),
                Entities = audit.Lines
                    .Select(line => new AuditEntityInput(
                        "StockTransferItem",
                        $"{audit.TransferId}:variant:{line.VariantId}",
                        AuditOperationType.Update,
                        [
                            ("AcceptedQuantity", null, line.AcceptedQuantity),
                            ("RejectedQuantity", null, line.RejectedQuantity),
                            ("MissingQuantity", null, line.MissingQuantity),
                            ("DamagedQuantity", null, line.DamagedQuantity)
                        ]))
                    .ToArray(),
                Metadata = new Dictionary<string, object?>
                {
                    ["transferNo"] = audit.TransferNo,
                    ["status"] = audit.Status,
                    ["locationId"] = audit.LocationId,
                    ["locationType"] = audit.LocationType,
                    ["actorUserId"] = audit.ActorUserId,
                    ["reason"] = audit.Reason,
                    ["lines"] = audit.Lines
                }
            },
            cancellationToken);
}
