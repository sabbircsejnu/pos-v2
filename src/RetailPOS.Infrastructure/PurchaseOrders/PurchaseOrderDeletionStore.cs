using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.PurchaseOrders.Abstractions;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.PurchaseOrders;

public sealed class PurchaseOrderDeletionStore(RetailPOSDbContext dbContext)
    : IPurchaseOrderDeletionStore
{
    public async Task<bool> CanDeleteAsync(
        long purchaseOrderId,
        long? businessId,
        CancellationToken cancellationToken)
    {
        var query = dbContext.PurchaseOrders
            .Include(order => order.Grns)
            .AsQueryable();
        if (businessId.HasValue)
            query = query.WhereBusiness(businessId.Value);

        var purchaseOrder = await query.FirstOrDefaultAsync(
            order => order.Id == purchaseOrderId,
            cancellationToken);
        if (purchaseOrder is null)
            return false;

        var allowedStatuses = new[] { "draft", "pending" };
        return allowedStatuses.Contains(purchaseOrder.Status.ToLower()) &&
               !purchaseOrder.Grns.Any();
    }

    public async Task<bool> DeleteAsync(
        long purchaseOrderId,
        long? businessId,
        CancellationToken cancellationToken)
    {
        var query = dbContext.PurchaseOrders
            .Include(order => order.Items)
            .AsQueryable();
        if (businessId.HasValue)
            query = query.WhereBusiness(businessId.Value);

        var purchaseOrder = await query.FirstOrDefaultAsync(
            order => order.Id == purchaseOrderId,
            cancellationToken);
        if (purchaseOrder is null)
            return false;

        dbContext.PurchaseOrderItems.RemoveRange(purchaseOrder.Items);
        dbContext.PurchaseOrders.Remove(purchaseOrder);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
