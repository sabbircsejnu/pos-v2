using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.PurchaseOrders.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.PurchaseOrders;

public sealed class PurchaseOrderCreationStore(RetailPOSDbContext dbContext)
    : IPurchaseOrderCreationStore
{
    public async Task<PurchaseOrderLocationScope?> FindLocationAsync(
        PurchaseOrderDestination destination,
        long? businessId,
        CancellationToken cancellationToken)
    {
        if (destination.LocationType == PurchaseLocationTypes.Outlet)
        {
            var outlets = dbContext.Outlets.Where(outlet => outlet.Id == destination.LocationId);
            if (businessId.HasValue)
                outlets = outlets.Where(outlet => outlet.BusinessId == businessId.Value);

            return await outlets
                .Select(outlet => new PurchaseOrderLocationScope(outlet.BusinessId, outlet.Name))
                .FirstOrDefaultAsync(cancellationToken);
        }

        var warehouses = dbContext.Warehouses.Where(warehouse => warehouse.Id == destination.LocationId);
        if (businessId.HasValue)
            warehouses = warehouses.Where(warehouse => warehouse.BusinessId == businessId.Value);

        return await warehouses
            .Select(warehouse => new PurchaseOrderLocationScope(warehouse.BusinessId, warehouse.Name))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<bool> SupplierExistsAsync(
        long supplierId,
        long businessId,
        CancellationToken cancellationToken) =>
        dbContext.Suppliers.AnyAsync(
            supplier => supplier.Id == supplierId && supplier.BusinessId == businessId,
            cancellationToken);

    public Task<bool> ProductVariantExistsAsync(
        long variantId,
        CancellationToken cancellationToken) =>
        dbContext.ProductVariants.AnyAsync(
            variant => variant.Id == variantId,
            cancellationToken);

    public async Task<long> CreateAsync(
        PurchaseOrder purchaseOrder,
        CancellationToken cancellationToken)
    {
        purchaseOrder.CreatedAt = DateTime.UtcNow;
        purchaseOrder.UpdatedAt = DateTime.UtcNow;
        await dbContext.PurchaseOrders.AddAsync(purchaseOrder, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return purchaseOrder.Id;
    }

    public async Task SetPoNumberAsync(
        long purchaseOrderId,
        string poNumber,
        CancellationToken cancellationToken)
    {
        var purchaseOrder = await dbContext.PurchaseOrders.FindAsync(
            [purchaseOrderId],
            cancellationToken);
        if (purchaseOrder is null)
            return;

        purchaseOrder.PoNumber = poNumber;
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
