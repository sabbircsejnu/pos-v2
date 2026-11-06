using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Inventory.StockTransfers;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.StockTransfers;

internal static class StockTransferWriteRules
{
    public static async Task ValidateBusinessLocationsAndSourceStockAsync(
        RetailPOSDbContext dbContext,
        StockTransferCreateInput input,
        long businessId,
        CancellationToken cancellationToken)
    {
        if (!await LocationBelongsToBusinessAsync(
                dbContext,
                input.FromLocationId,
                input.FromLocationType,
                businessId,
                cancellationToken)
            || !await LocationBelongsToBusinessAsync(
                dbContext,
                input.ToLocationId,
                input.ToLocationType,
                businessId,
                cancellationToken))
        {
            throw new UnauthorizedAccessException(
                "Stock transfer locations must belong to the current business.");
        }

        foreach (var item in input.Items)
        {
            var variantSku = await dbContext.ProductVariants
                .Where(variant => variant.Id == item.VariantId)
                .Select(variant => variant.Sku)
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new KeyNotFoundException(
                    $"Product variant with ID {item.VariantId} not found");

            var sourceInventory = await dbContext.Inventories.FirstOrDefaultAsync(inventory =>
                inventory.VariantId == item.VariantId
                && inventory.LocationId == input.FromLocationId
                && inventory.LocationType.ToLower() == input.FromLocationType,
                cancellationToken);

            var transferQuantity = ResolveTransferQuantity(item);
            if (sourceInventory is null || sourceInventory.Quantity < transferQuantity)
            {
                throw new InvalidOperationException(
                    $"Insufficient stock for variant {variantSku}. Available: {sourceInventory?.Quantity ?? 0}, Requested: {transferQuantity}");
            }
        }
    }

    public static IQueryable<StockTransfer> TransfersInBusiness(
        RetailPOSDbContext dbContext,
        long businessId) =>
        dbContext.StockTransfers.Where(candidate =>
            ((candidate.FromLocationType.ToLower() == "outlet"
                && dbContext.Outlets.Any(location =>
                    location.Id == candidate.FromLocationId
                    && location.BusinessId == businessId))
             || (candidate.FromLocationType.ToLower() == "warehouse"
                && dbContext.Warehouses.Any(location =>
                    location.Id == candidate.FromLocationId
                    && location.BusinessId == businessId)))
            && ((candidate.ToLocationType.ToLower() == "outlet"
                && dbContext.Outlets.Any(location =>
                    location.Id == candidate.ToLocationId
                    && location.BusinessId == businessId))
             || (candidate.ToLocationType.ToLower() == "warehouse"
                && dbContext.Warehouses.Any(location =>
                    location.Id == candidate.ToLocationId
                    && location.BusinessId == businessId))));

    /// <summary>
    /// Puts rejected units in return transit. They never re-entered destination stock, so nothing is deducted here;
    /// source stock rises only when the source receives this return. The unique index allows one per transfer.
    /// </summary>
    public static async Task CreateSystemReturnAsync(
        RetailPOSDbContext dbContext,
        StockTransfer original,
        long? actorId,
        CancellationToken cancellationToken)
    {
        var lines = original.Items.Where(item => item.RejectedQuantity > 0).ToArray();
        if (lines.Length == 0)
            return;

        var now = DateTime.UtcNow;
        var maxId = await dbContext.StockTransfers
            .MaxAsync(candidate => (long?)candidate.Id, cancellationToken) ?? 0;
        dbContext.StockTransfers.Add(new StockTransfer
        {
            TransferNo = $"TRF-{now.Year}-{(maxId + 1):D5}",
            TransferType = StockTransfer.TransferTypeReturn,
            OriginalTransferId = original.Id,
            FromLocationId = original.ToLocationId,
            FromLocationType = original.ToLocationType,
            ToLocationId = original.FromLocationId,
            ToLocationType = original.FromLocationType,
            TransferDate = now,
            Status = StockTransfer.StatusInTransit,
            DispatchedBy = actorId,
            DispatchedAt = now,
            CreatedBy = actorId,
            CreatedAt = now,
            Notes = $"Auto-created return transfer for rejected transfer #{original.TransferNo ?? original.Id.ToString()}.",
            Items = lines.Select(item => new StockTransferItem
            {
                VariantId = item.VariantId,
                Quantity = item.RejectedQuantity,
                RequestedQuantity = item.RejectedQuantity,
                TransferQuantity = item.RejectedQuantity,
                UnitCost = item.UnitCost
            }).ToList()
        });
    }

    /// <summary>
    /// A movement that changes no quantity still takes a ledger sequence for this location, so it must conflict with
    /// any other movement there. Saving the unchanged row bumps xmin and gives it that protection.
    /// </summary>
    public static void ClaimForEvidenceOnlyMovement(RetailPOSDbContext dbContext, Inventory? inventory)
    {
        if (inventory is not null && dbContext.Entry(inventory).State == EntityState.Unchanged)
            dbContext.Entry(inventory).Property(row => row.Quantity).IsModified = true;
    }

    public static string NormalizeTransferType(string? transferType)
    {
        var normalized = string.IsNullOrWhiteSpace(transferType)
            ? StockTransfer.TransferTypeDirect
            : transferType.Trim().ToLowerInvariant();

        if (normalized != StockTransfer.TransferTypeDirect
            && normalized != StockTransfer.TransferTypeRequisition
            && normalized != StockTransfer.TransferTypeReturn)
        {
            throw new InvalidOperationException($"Invalid transfer type '{transferType}'.");
        }

        return normalized;
    }

    public static int ResolveRequestedQuantity(StockTransferCreateItem item)
    {
        if (item.RequestedQuantity.HasValue && item.RequestedQuantity.Value > 0)
            return item.RequestedQuantity.Value;
        if (item.Quantity > 0)
            return item.Quantity;
        if (item.TransferQuantity.HasValue && item.TransferQuantity.Value > 0)
            return item.TransferQuantity.Value;
        return item.Quantity;
    }

    public static int ResolveTransferQuantity(StockTransferCreateItem item)
    {
        if (item.TransferQuantity.HasValue && item.TransferQuantity.Value > 0)
            return item.TransferQuantity.Value;
        if (item.Quantity > 0)
            return item.Quantity;
        if (item.RequestedQuantity.HasValue && item.RequestedQuantity.Value > 0)
            return item.RequestedQuantity.Value;
        return item.Quantity;
    }

    public static Task<bool> LocationBelongsToBusinessAsync(
        RetailPOSDbContext dbContext,
        long locationId,
        string locationType,
        long businessId,
        CancellationToken cancellationToken) =>
        locationType == "outlet"
            ? dbContext.Outlets.AnyAsync(
                location => location.Id == locationId && location.BusinessId == businessId,
                cancellationToken)
            : dbContext.Warehouses.AnyAsync(
                location => location.Id == locationId && location.BusinessId == businessId,
                cancellationToken);
}
