using RetailPOS.Core.Entities;

namespace RetailPOS.Application.Grns.Abstractions;

public interface IGrnCompletionAccountingPoster
{
    Task<long?> PostReceiptAndCreatePayableAsync(
        Grn grn,
        PurchaseOrder purchaseOrder,
        long businessId,
        long actorUserId,
        CancellationToken cancellationToken);
}
