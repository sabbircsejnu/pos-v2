using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.PurchaseOrders.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.PurchaseOrders;

public sealed class PurchaseOrderUpdateStore(RetailPOSDbContext dbContext)
    : IPurchaseOrderUpdateStore
{
    public async Task<PurchaseOrder?> FindForUpdateAsync(
        long purchaseOrderId,
        long? businessId,
        CancellationToken cancellationToken)
    {
        var query = dbContext.PurchaseOrders
            .Include(order => order.Supplier)
            .Include(order => order.Warehouse)
            .Include(order => order.Outlet)
            .Include(order => order.Creator)
            .Include(order => order.Grns)
            .Include(order => order.Items)
                .ThenInclude(item => item.Variant)
                    .ThenInclude(variant => variant.Product)
            .AsQueryable();

        if (businessId.HasValue)
            query = query.WhereBusiness(businessId.Value);

        return await query.FirstOrDefaultAsync(
            order => order.Id == purchaseOrderId,
            cancellationToken);
    }

    public async Task SaveUpdateAsync(
        PurchaseOrder purchaseOrder,
        CancellationToken cancellationToken)
    {
        purchaseOrder.UpdatedAt = DateTime.UtcNow;
        dbContext.PurchaseOrders.Update(purchaseOrder);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveStatusUpdateAsync(
        PurchaseOrder purchaseOrder,
        string status,
        CancellationToken cancellationToken)
    {
        purchaseOrder.Status = status;
        purchaseOrder.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
