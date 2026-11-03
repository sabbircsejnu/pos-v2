namespace RetailPOS.Application.PurchaseOrders.Abstractions;

public interface IPurchaseOrderDeletionStore
{
    Task<bool> CanDeleteAsync(
        long purchaseOrderId,
        long? businessId,
        CancellationToken cancellationToken);

    Task<bool> DeleteAsync(
        long purchaseOrderId,
        long? businessId,
        CancellationToken cancellationToken);
}
