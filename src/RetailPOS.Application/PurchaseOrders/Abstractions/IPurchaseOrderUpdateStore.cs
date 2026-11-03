using RetailPOS.Core.Entities;

namespace RetailPOS.Application.PurchaseOrders.Abstractions;

public interface IPurchaseOrderUpdateStore
{
    Task<PurchaseOrder?> FindForUpdateAsync(
        long purchaseOrderId,
        long? businessId,
        CancellationToken cancellationToken);

    Task SaveUpdateAsync(
        PurchaseOrder purchaseOrder,
        CancellationToken cancellationToken);

    Task SaveStatusUpdateAsync(
        PurchaseOrder purchaseOrder,
        string status,
        CancellationToken cancellationToken);
}
