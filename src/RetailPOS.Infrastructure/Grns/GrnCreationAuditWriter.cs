using RetailPOS.Application.Grns.Abstractions;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Audit;

namespace RetailPOS.Infrastructure.Grns;

public sealed class GrnCreationAuditWriter(IAuditService auditService)
    : IGrnCreationAuditWriter
{
    public Task RecordCreatedAsync(
        GrnCreationAuditRecord record,
        CancellationToken cancellationToken) =>
        auditService.RecordAsync(new AuditEventInput
        {
            ActionType = AuditActionType.Create,
            Module = AuditModule.Purchase,
            Summary = $"GRN created: {record.GrnId}",
            PrimaryEntity = ("Grn", record.GrnId.ToString()),
            Metadata = new Dictionary<string, object?>
            {
                ["businessId"] = record.BusinessId,
                ["warehouseId"] = record.LocationType == "warehouse" ? record.LocationId : null,
                ["outletId"] = record.LocationType == "outlet" ? record.LocationId : null,
                ["locationType"] = record.LocationType,
                ["locationId"] = record.LocationId,
                ["supplierId"] = record.SupplierId,
                ["poId"] = record.PurchaseOrderId,
                ["grnId"] = record.GrnId,
                ["journalEntryId"] = null,
                ["actorUserId"] = record.ActorUserId
            },
            Entities =
            [
                new AuditEntityInput(
                    "Grn",
                    record.GrnId.ToString(),
                    AuditOperationType.Insert,
                    [
                        ("Status", null, record.Status),
                        ("ReceivedDate", null, record.ReceivedDate),
                        ("ItemCount", null, record.ItemCount),
                        ("ReceivedQuantity", null, record.ReceivedQuantity),
                        ("TaxableAmount", null, record.TaxableAmount),
                        ("VatAmount", null, record.VatAmount)
                    ])
            ]
        }, cancellationToken);
}
