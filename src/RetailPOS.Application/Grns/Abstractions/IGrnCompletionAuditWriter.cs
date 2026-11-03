using RetailPOS.Core.Entities;

namespace RetailPOS.Application.Grns.Abstractions;

public interface IGrnCompletionAuditWriter
{
    Task RecordCompletedAsync(
        Grn grn,
        PurchaseOrder purchaseOrder,
        long actorUserId,
        long? businessId,
        long? journalEntryId,
        CancellationToken cancellationToken);
}
