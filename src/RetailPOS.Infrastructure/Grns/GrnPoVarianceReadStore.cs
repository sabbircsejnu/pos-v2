using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Grns.Abstractions;
using RetailPOS.Application.Grns.ReadModels;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Grns;

public sealed class GrnPoVarianceReadStore(RetailPOSDbContext dbContext) : IGrnPoVarianceReadStore
{
    public async Task<GrnPoVarianceReadModel?> FindByPurchaseOrderIdAsync(
        long purchaseOrderId,
        GrnReadScope scope,
        CancellationToken cancellationToken)
    {
        var purchaseOrder = await dbContext.PurchaseOrders
            .AsNoTracking()
            .Include(order => order.Items)
                .ThenInclude(item => item.Variant)
                    .ThenInclude(variant => variant.Product)
            .WhereReadScope(scope)
            .FirstOrDefaultAsync(
            order => order.Id == purchaseOrderId,
            cancellationToken);
        if (purchaseOrder is null)
            return null;

        var grns = await dbContext.Grns
            .AsNoTracking()
            .Where(grn => grn.PoId == purchaseOrderId)
            .Include(grn => grn.Items)
            .ToListAsync(cancellationToken);

        var completedGrns = grns.Where(grn => grn.Status == "completed").ToArray();
        var receivedByItem = completedGrns
            .SelectMany(grn => grn.Items)
            .GroupBy(item => item.PoItemId)
            .ToDictionary(
                group => group.Key,
                group => (
                    Quantity: group.Sum(item => item.ReceivedQty),
                    Cost: group.Sum(item => item.ReceivedQty * item.UnitCost)));

        return new GrnPoVarianceReadModel(
            purchaseOrder.Id,
            purchaseOrder.Status,
            grns.Count,
            completedGrns.Length,
            purchaseOrder.Items.Select(item =>
            {
                receivedByItem.TryGetValue(item.Id, out var received);
                return new GrnPoVarianceItemReadModel(
                    item.Id,
                    item.VariantId,
                    item.Variant?.Product?.Name ?? string.Empty,
                    item.Variant?.Name ?? string.Empty,
                    item.Quantity,
                    received.Quantity,
                    item.UnitPrice,
                    received.Cost);
            }).ToArray());
    }
}
