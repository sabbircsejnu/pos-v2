using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RetailPOS.Application.Grns.Abstractions;
using RetailPOS.Application.Grns.ReadModels;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.PurchaseOrders;

namespace RetailPOS.Infrastructure.Grns;

public sealed class GrnCompletionStore(
    RetailPOSDbContext dbContext,
    IGrnCompletionStockLedgerWriter stockLedgerWriter,
    IGrnCompletionAccountingPoster accountingPoster,
    IGrnCompletionAuditWriter auditWriter,
    ILogger<GrnCompletionStore> logger) : IGrnCompletionStore
{
    public Task<GrnCompletionScopeReadModel?> FindScopeAsync(
        long grnId,
        CancellationToken cancellationToken) =>
        dbContext.Grns
            .AsNoTracking()
            .Where(grn => grn.Id == grnId)
            .Select(grn => new GrnCompletionScopeReadModel(
                grn.PurchaseOrder.OutletId != null
                    ? grn.PurchaseOrder.Outlet!.BusinessId
                    : grn.PurchaseOrder.Warehouse!.BusinessId,
                grn.PurchaseOrder.OutletId ?? grn.PurchaseOrder.WarehouseId ?? 0,
                grn.PurchaseOrder.OutletId != null
                    ? PurchaseLocationTypes.Outlet
                    : PurchaseLocationTypes.Warehouse,
                grn.Status))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<GrnReadModel> CompleteAsync(
        long grnId,
        long actorUserId,
        long? businessId,
        CancellationToken cancellationToken)
    {
        var grn = await dbContext.Grns
            .Include(receipt => receipt.PurchaseOrder)
                .ThenInclude(order => order.Supplier)
            .Include(receipt => receipt.PurchaseOrder)
                .ThenInclude(order => order.Warehouse)
            .Include(receipt => receipt.PurchaseOrder)
                .ThenInclude(order => order.Outlet)
            .Include(receipt => receipt.PurchaseOrder)
                .ThenInclude(order => order.Creator)
            .Include(receipt => receipt.Creator)
            .Include(receipt => receipt.Items)
                .ThenInclude(item => item.PurchaseOrderItem)
                    .ThenInclude(item => item.Variant)
                        .ThenInclude(variant => variant.Product)
            .Where(receipt => receipt.Id == grnId)
            .WhereBusiness(businessId)
            .FirstOrDefaultAsync(cancellationToken);

        if (grn is null)
            throw new KeyNotFoundException($"GRN with ID {grnId} not found");

        if (grn.Status == "completed")
            throw new InvalidOperationException("GRN is already completed");

        var purchaseOrder = grn.PurchaseOrder;
        // Receive into the destination location selected on the purchase order.
        var locationId = purchaseOrder.LocationId;
        var locationType = purchaseOrder.LocationType;

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (var item in grn.Items)
            {
                var variantId = item.PurchaseOrderItem.VariantId;
                var inventory = await dbContext.Inventories
                    .FirstOrDefaultAsync(
                        row => row.VariantId == variantId
                            && row.LocationId == locationId
                            && row.LocationType.ToLower() == locationType,
                        cancellationToken);

                if (inventory is null)
                {
                    inventory = new Inventory
                    {
                        VariantId = variantId,
                        LocationId = locationId,
                        LocationType = locationType,
                        Quantity = item.ReceivedQty
                    };
                    dbContext.Inventories.Add(inventory);
                }
                else
                {
                    inventory.Quantity += item.ReceivedQty;
                }

                stockLedgerWriter.WriteEntryWithValuation(
                    variantId,
                    locationId,
                    locationType,
                    StockLedgerTransactionType.Grn,
                    item.ReceivedQty,
                    0,
                    inventory.Quantity,
                    StockLedgerReferenceType.Grn,
                    grn.Id,
                    $"GRN completion — PO-{grn.PoId:D6}",
                    actorUserId,
                    item.UnitCost);
            }

            var journalEntryId = await accountingPoster.PostReceiptAndCreatePayableAsync(
                grn,
                purchaseOrder,
                purchaseOrder.LocationBusinessId
                    ?? throw new InvalidOperationException("GRN destination location must belong to a Business."),
                actorUserId,
                cancellationToken);

            grn.Status = "completed";
            var allGrnsForPurchaseOrder = await dbContext.Grns
                .Include(receipt => receipt.Items)
                .Where(receipt => receipt.PoId == purchaseOrder.Id
                    && receipt.Status == "completed")
                .ToListAsync(cancellationToken);

            var receivedQuantityByItem = allGrnsForPurchaseOrder
                .SelectMany(receipt => receipt.Items)
                .Where(item => item.GrnId != grn.Id)
                .GroupBy(item => item.PoItemId)
                .ToDictionary(group => group.Key, group => group.Sum(item => item.ReceivedQty));

            foreach (var item in grn.Items)
            {
                if (receivedQuantityByItem.TryGetValue(item.PoItemId, out var receivedQuantity))
                    receivedQuantityByItem[item.PoItemId] = receivedQuantity + item.ReceivedQty;
                else
                    receivedQuantityByItem[item.PoItemId] = item.ReceivedQty;
            }

            var allReceived = purchaseOrder.Items.All(item =>
                receivedQuantityByItem.TryGetValue(item.Id, out var receivedQuantity)
                && receivedQuantity >= item.Quantity);
            var newPurchaseOrderStatus = allReceived ? "received" : "partial";
            purchaseOrder.Status = newPurchaseOrderStatus;
            purchaseOrder.UpdatedAt = DateTime.UtcNow;

            await dbContext.SaveChangesAsync(cancellationToken);
            await auditWriter.RecordCompletedAsync(
                grn,
                purchaseOrder,
                actorUserId,
                purchaseOrder.LocationBusinessId,
                journalEntryId,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            logger.LogInformation(
                "GRN {GrnId} completed. PO {PoId} status updated to {Status}",
                grnId,
                purchaseOrder.Id,
                newPurchaseOrderStatus);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }

        return GrnReadModelMapper.Map(grn);
    }
}
