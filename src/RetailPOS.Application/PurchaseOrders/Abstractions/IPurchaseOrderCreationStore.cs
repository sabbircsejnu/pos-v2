namespace RetailPOS.Application.PurchaseOrders.Abstractions;

public interface IPurchaseOrderCreationStore
{
    /// <summary>
    /// Finds an Outlet or Warehouse destination; when <paramref name="businessId"/> is supplied the
    /// location must belong to that Business.
    /// </summary>
    Task<PurchaseOrderLocationScope?> FindLocationAsync(
        PurchaseOrderDestination destination,
        long? businessId,
        CancellationToken cancellationToken);

    Task<bool> SupplierExistsAsync(
        long supplierId,
        long businessId,
        CancellationToken cancellationToken);

    Task<bool> ProductVariantExistsAsync(
        long variantId,
        CancellationToken cancellationToken);

    Task<long> CreateAsync(
        RetailPOS.Core.Entities.PurchaseOrder purchaseOrder,
        CancellationToken cancellationToken);

    Task SetPoNumberAsync(
        long purchaseOrderId,
        string poNumber,
        CancellationToken cancellationToken);
}
