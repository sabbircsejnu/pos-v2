using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RetailPOS.Application.Inventory.Costing;
using RetailPOS.Application.PurchaseOrders.Abstractions;
using RetailPOS.Application.PurchaseOrders.Commands;
using RetailPOS.Application.Uom;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Money;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.PurchaseOrders;

public sealed class PurchaseAndReceiveStore(
    RetailPOSDbContext dbContext,
    IPurchaseAndReceiveStockLedgerWriter stockLedgerWriter,
    IPurchaseReceiptJournalPoster journalPoster,
    ILogger<PurchaseAndReceiveStore> logger,
    RetailPOS.Application.Uom.IPackStateLedger? packState = null)
    : IPurchaseAndReceiveStore
{
    public async Task<PurchaseAndReceiveStoreResult> ExecuteAsync(
        PurchaseAndReceiveCommand command,
        long? businessId,
        CancellationToken cancellationToken)
    {
        var idempotencyKey = command.IdempotencyKey
            ?? throw new InvalidOperationException("Idempotency key is required for purchase and receive action");
        var destination = command.Destination;
        var existingOrder = await FindByIdempotencyKeyAsync(
            destination,
            idempotencyKey,
            businessId,
            cancellationToken);
        if (existingOrder is not null)
        {
            return new PurchaseAndReceiveStoreResult(
                existingOrder.Id,
                existingOrder.Grns.OrderByDescending(grn => grn.Id).Select(grn => grn.Id).FirstOrDefault(),
                true);
        }

        var items = command.Items;
        if (items is null || items.Count == 0)
            throw new InvalidOperationException("Purchase order must have at least one item");

        var locationBusinessId = await FindLocationBusinessIdAsync(destination, businessId, cancellationToken);

        var supplierBusinessId = businessId ?? locationBusinessId
            ?? throw new InvalidOperationException("Destination location business is required.");
        if (!await dbContext.Suppliers.AnyAsync(
                supplier => supplier.Id == command.SupplierId
                    && supplier.BusinessId == supplierBusinessId,
                cancellationToken))
            throw new KeyNotFoundException($"Supplier with ID {command.SupplierId} not found");

        foreach (var item in items)
        {
            if (!await dbContext.ProductVariants.AnyAsync(
                    variant => variant.Id == item.VariantId,
                    cancellationToken))
                throw new KeyNotFoundException($"Product variant with ID {item.VariantId} not found");
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var totalAmount = items.Sum(item =>
                MonetaryRounding.ToCurrency(
                    item.Quantity * item.UnitPrice
                    * (1 - item.Discount / 100m)
                    * (1 + item.Tax / 100m)));
            var purchaseOrder = new PurchaseOrder
            {
                SupplierId = command.SupplierId,
                IdempotencyKey = idempotencyKey,
                OrderDate = DateTime.SpecifyKind(command.OrderDate, DateTimeKind.Utc),
                ExpectedDelivery = command.ExpectedDelivery.HasValue
                    ? DateTime.SpecifyKind(command.ExpectedDelivery.Value, DateTimeKind.Utc)
                    : null,
                Status = "approved",
                Notes = command.Notes,
                TotalAmount = totalAmount,
                CreatedBy = command.UserId,
                Items = items.Select(item =>
                {
                    var orderItem = new PurchaseOrderItem
                    {
                        VariantId = item.VariantId,
                        Quantity = item.Quantity,
                        UnitPrice = item.UnitPrice,
                        Discount = item.Discount,
                        Tax = item.Tax,
                        Unit = item.Unit
                    };
                    if (item.Snapshot is not null)
                        orderItem.CaptureUomSnapshot(item.Snapshot);
                    return orderItem;
                }).ToList()
            };
            purchaseOrder.SetDestination(destination.LocationType, destination.LocationId);
            dbContext.PurchaseOrders.Add(purchaseOrder);
            await dbContext.SaveChangesAsync(cancellationToken);

            purchaseOrder.PoNumber = GeneratePoNumber(purchaseOrder.Id, purchaseOrder.OrderDate);
            await dbContext.SaveChangesAsync(cancellationToken);

            var grn = new Grn
            {
                PoId = purchaseOrder.Id,
                ReceivedDate = DateTime.UtcNow,
                Status = "full",
                Notes = $"System-generated GRN created from Purchase Order {purchaseOrder.PoNumber} by immediate purchase receive action.\nType: AUTO_GENERATED_FROM_PURCHASE_ORDER",
                CreatedBy = command.UserId,
                Items = purchaseOrder.Items.Select(item => new GrnItem
                {
                    PoItemId = item.Id,
                    ReceivedQty = item.Quantity,
                    UnitCost = item.UnitPrice,
                    TaxableAmount = MonetaryRounding.ToCurrency(
                        item.Quantity * item.UnitPrice * (1 - item.Discount / 100m)),
                    VatRate = item.Tax,
                    VatAmount = MonetaryRounding.ToCurrency(
                        item.Quantity * item.UnitPrice
                        * (1 - item.Discount / 100m)
                        * item.Tax / 100m),
                    TaxCode = item.Tax > 0 ? "VAT" : null,
                    TaxInclusive = item.TaxInclusive,
                    Notes = "AUTO_GENERATED_FROM_PURCHASE_ORDER"
                }).ToList()
            };
            dbContext.Grns.Add(grn);
            await dbContext.SaveChangesAsync(cancellationToken);

            var locationId = destination.LocationId;
            var locationType = destination.LocationType;
            foreach (var receiptItem in grn.Items)
            {
                var purchaseOrderItem = purchaseOrder.Items.First(item => item.Id == receiptItem.PoItemId);
                var inventory = await dbContext.Inventories.FirstOrDefaultAsync(
                    row => row.VariantId == purchaseOrderItem.VariantId
                        && row.LocationId == locationId
                        && row.LocationType.ToLower() == locationType,
                    cancellationToken);
                if (inventory is null)
                {
                    inventory = new Inventory
                    {
                        VariantId = purchaseOrderItem.VariantId,
                        LocationId = locationId,
                        LocationType = locationType,
                        Quantity = receiptItem.ReceivedQty
                    };
                    dbContext.Inventories.Add(inventory);
                }
                else
                {
                    inventory.Quantity += receiptItem.ReceivedQty;
                }

                stockLedgerWriter.WriteEntry(
                    purchaseOrderItem.VariantId,
                    locationId,
                    locationType,
                    StockLedgerTransactionType.Grn,
                    receiptItem.ReceivedQty,
                    0,
                    inventory.Quantity,
                    StockLedgerReferenceType.Grn,
                    grn.Id,
                    $"System auto-GRN from {purchaseOrder.PoNumber}",
                    command.UserId,
                    // The value PurchaseReceiptJournalPoster debits to Inventory for the line.
                    GrnLedgerCost.Resolve(
                        receiptItem.ReceivedQty,
                        receiptItem.UnitCost,
                        receiptItem.ReceivedQty * receiptItem.UnitCost,
                        purchaseOrderItem.UnitPrice,
                        purchaseOrderItem.Discount));

                var commandItem = items.FirstOrDefault(item => item.VariantId == purchaseOrderItem.VariantId && item.TrackPackState);
                if (commandItem is not null && packState is not null)
                {
                    var breakdown = PurchaseUomRules.Breakdown(
                        receiptItem.ReceivedQty, commandItem.AcceptedSealedPacks, commandItem.AcceptedLooseQuantity,
                        purchaseOrderItem.ReadUomSnapshot(), commandItem.IsSealedPackLine);
                    receiptItem.AcceptedSealedPacks = breakdown.SealedPacks;
                    receiptItem.AcceptedLooseQuantity = breakdown.LooseQuantity;
                    await PurchaseUomRules.ApplyReceiptAsync(
                        packState, supplierBusinessId, purchaseOrderItem.VariantId, locationId, locationType, grn.Id, command.UserId,
                        breakdown, inventory.Quantity, cancellationToken);
                }
            }

            await journalPoster.PostAsync(
                grn,
                purchaseOrder,
                supplierBusinessId,
                cancellationToken);
            grn.Status = "completed";
            purchaseOrder.Status = "completed";
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            logger.LogInformation(
                "Immediate purchase receive completed for PO {PurchaseOrderId}; GRN {GrnId} created by user {UserId}",
                purchaseOrder.Id,
                grn.Id,
                command.UserId);
            return new PurchaseAndReceiveStoreResult(purchaseOrder.Id, grn.Id, false);
        }
        catch (DbUpdateException exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            var duplicateOrder = await FindByIdempotencyKeyAsync(
                destination,
                idempotencyKey,
                businessId,
                cancellationToken);
            if (duplicateOrder is not null)
            {
                logger.LogWarning(
                    exception,
                    "Duplicate immediate receive request detected for {LocationType} {LocationId} and IdempotencyKey {IdempotencyKey}",
                    destination.LocationType,
                    destination.LocationId,
                    idempotencyKey);
                return new PurchaseAndReceiveStoreResult(
                    duplicateOrder.Id,
                    duplicateOrder.Grns.OrderByDescending(grn => grn.Id).Select(grn => grn.Id).FirstOrDefault(),
                    true);
            }

            throw;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private Task<PurchaseOrder?> FindByIdempotencyKeyAsync(
        PurchaseOrderDestination destination,
        string idempotencyKey,
        long? businessId,
        CancellationToken cancellationToken) =>
        dbContext.PurchaseOrders
            .AsNoTracking()
            .Include(order => order.Grns)
            .WhereDestination(destination.LocationType, destination.LocationId)
            .Where(order => order.IdempotencyKey == idempotencyKey)
            .WhereBusiness(businessId)
            .OrderByDescending(order => order.Id)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<long?> FindLocationBusinessIdAsync(
        PurchaseOrderDestination destination,
        long? businessId,
        CancellationToken cancellationToken)
    {
        if (destination.LocationType == PurchaseLocationTypes.Outlet)
        {
            var outlet = await dbContext.Outlets
                .AsNoTracking()
                .Where(row => row.Id == destination.LocationId
                    && (!businessId.HasValue || row.BusinessId == businessId.Value))
                .Select(row => new { row.BusinessId })
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new KeyNotFoundException($"Outlet with ID {destination.LocationId} not found");
            return outlet.BusinessId;
        }

        var warehouse = await dbContext.Warehouses
            .AsNoTracking()
            .Where(row => row.Id == destination.LocationId
                && (!businessId.HasValue || row.BusinessId == businessId.Value))
            .Select(row => new { row.BusinessId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Warehouse with ID {destination.LocationId} not found");
        return warehouse.BusinessId;
    }

    private static string GeneratePoNumber(long id, DateTime orderDate) =>
        $"PO-{orderDate:yyyyMMdd}-{id:D4}";
}
