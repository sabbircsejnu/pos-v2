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
            Items = lines.Select(item =>
            {
                var returned = new StockTransferItem
                {
                    VariantId = item.VariantId,
                    Quantity = item.RejectedQuantity,
                    RequestedQuantity = item.RejectedQuantity,
                    TransferQuantity = item.RejectedQuantity,
                    UnitCost = item.UnitCost
                };
                // The rejected stock goes back in the form it came: whole packs as packs, the rest loose.
                var wholeLine = item.RejectedQuantity == item.Allocations.Sum(allocation => allocation.BaseQuantity);
                if (wholeLine)
                    foreach (var allocation in item.Allocations)
                        returned.Allocations.Add(new RetailPOS.Core.Entities.Uom.StockTransferItemAllocation
                        {
                            State = allocation.State,
                            PackCode = allocation.PackCode,
                            BaseQtyPerPack = allocation.BaseQtyPerPack,
                            PackCount = allocation.PackCount,
                            BaseQuantity = allocation.BaseQuantity,
                            CapitalizedValue = allocation.CapitalizedValue
                        });
                else if (item.Allocations.Count > 0)
                    returned.Allocations.Add(new RetailPOS.Core.Entities.Uom.StockTransferItemAllocation
                    {
                        State = RetailPOS.Core.Entities.Uom.PackStates.Loose,
                        BaseQuantity = item.RejectedQuantity
                    });
                return returned;
            }).ToList()
        });
    }

    /// <summary>Copies the unit interpretation of an entered line onto its stored transfer item.</summary>
    public static StockTransferItem WithUom(this StockTransferItem target, StockTransferCreateItem item)
    {
        if (item.Snapshot is not null)
            target.CaptureUomSnapshot(item.Snapshot);
        foreach (var allocation in item.Allocations ?? [])
            target.Allocations.Add(new RetailPOS.Core.Entities.Uom.StockTransferItemAllocation
            {
                State = allocation.State,
                PackCode = allocation.PackCode ?? string.Empty,
                BaseQtyPerPack = allocation.BaseQtyPerPack,
                PackCount = allocation.PackCount,
                BaseQuantity = allocation.BaseQuantity
            });
        return target;
    }

    /// <summary>Takes the line's physical form out of the source when a pack-tracked item is dispatched.</summary>
    public static async Task DispatchPackStateAsync(
        RetailPOS.Application.Uom.IVariantUomReader? reader,
        RetailPOS.Application.Uom.IPackStateLedger? ledger,
        long businessId,
        StockTransfer transfer,
        StockTransferItem item,
        string locationType,
        decimal moved,
        decimal onHandAfter,
        long? actorId,
        CancellationToken cancellationToken)
    {
        if (reader is null || ledger is null)
            return;
        var configs = await reader.GetCurrentAsync(businessId, [item.VariantId], cancellationToken);
        if (configs.GetValueOrDefault(item.VariantId)?.PackStateTrackingEnabled != true)
            return;

        if (item.Allocations.Count == 0)
            item.Allocations.Add(new RetailPOS.Core.Entities.Uom.StockTransferItemAllocation
            {
                State = RetailPOS.Core.Entities.Uom.PackStates.Loose,
                BaseQuantity = moved
            });
        foreach (var allocation in item.Allocations)
        {
            if (allocation.State == RetailPOS.Core.Entities.Uom.PackStates.Prepared)
            {
                var balances = await ledger.GetBalancesForUpdateAsync(businessId, item.VariantId, transfer.FromLocationId, locationType, cancellationToken);
                var prepared = balances.FirstOrDefault(row => row.State == allocation.State && row.PackCode == allocation.PackCode && row.BaseQtyPerPack == allocation.BaseQtyPerPack);
                allocation.CapitalizedValue = prepared is null
                    ? 0m
                    : RetailPOS.Application.Uom.CapitalizedValueShare.For(prepared.CapitalizedValue, prepared.PackCount, allocation.PackCount);
            }

            await ledger.ApplyAsync(
                new RetailPOS.Application.Uom.PackStateChange(
                    businessId, item.VariantId, transfer.FromLocationId, locationType, allocation.State, allocation.PackCode, allocation.BaseQtyPerPack,
                    -allocation.PackCount, -allocation.BaseQuantity, -allocation.CapitalizedValue,
                    RetailPOS.Core.Entities.Uom.PackStateOperations.TransferOut, StockLedgerReferenceType.StockTransfer, transfer.Id, actorId),
                cancellationToken);
        }

        await ledger.EnsureReconciledAsync(businessId, item.VariantId, transfer.FromLocationId, locationType, onHandAfter, cancellationToken);
    }

    /// <summary>
    /// Puts accepted stock into the destination in the form it left. A sealed or prepared line is accepted or rejected
    /// whole: a partly missing pack has no approved value treatment, so it fails rather than guessing (STATE-006).
    /// </summary>
    public static async Task ReceivePackStateAsync(
        RetailPOS.Application.Uom.IVariantUomReader? reader,
        RetailPOS.Application.Uom.IPackStateLedger? ledger,
        long businessId,
        StockTransfer transfer,
        StockTransferItem item,
        string locationType,
        decimal dispatched,
        decimal accepted,
        decimal onHandAfter,
        long? actorId,
        CancellationToken cancellationToken)
    {
        if (reader is null || ledger is null)
            return;
        var configs = await reader.GetCurrentAsync(businessId, [item.VariantId], cancellationToken);
        if (configs.GetValueOrDefault(item.VariantId)?.PackStateTrackingEnabled != true)
            return;

        var physical = item.Allocations.Where(allocation => allocation.State != RetailPOS.Core.Entities.Uom.PackStates.Loose).ToList();
        if (physical.Count > 0 && accepted != dispatched && accepted != 0m)
            throw new RetailPOS.Application.Uom.UomValidationException(
                RetailPOS.Application.Uom.UomErrorCodes.UnsupportedOperation,
                $"Variant {item.VariantId} includes sealed or prepared packs. Accept or reject the whole line.");
        if (physical.Count > 0 && accepted == 0m && item.RejectedQuantity != dispatched)
            throw new RetailPOS.Application.Uom.UomValidationException(
                RetailPOS.Application.Uom.UomErrorCodes.UnsupportedOperation,
                $"Variant {item.VariantId} includes sealed or prepared packs. Missing or damaged packs cannot be recorded on this line; reject it and record the loss as an adjustment.");
        if (accepted <= 0m)
            return;

        if (accepted == dispatched && item.Allocations.Count > 0)
        {
            foreach (var allocation in item.Allocations)
                await ledger.ApplyAsync(
                    new RetailPOS.Application.Uom.PackStateChange(
                        businessId, item.VariantId, transfer.ToLocationId, locationType, allocation.State, allocation.PackCode, allocation.BaseQtyPerPack,
                        allocation.PackCount, allocation.BaseQuantity, allocation.CapitalizedValue,
                        RetailPOS.Core.Entities.Uom.PackStateOperations.TransferIn, StockLedgerReferenceType.StockTransfer, transfer.Id, actorId),
                    cancellationToken);
        }
        else
        {
            await ledger.ApplyAsync(
                new RetailPOS.Application.Uom.PackStateChange(
                    businessId, item.VariantId, transfer.ToLocationId, locationType, RetailPOS.Core.Entities.Uom.PackStates.Loose, string.Empty, 0m,
                    0, accepted, 0m,
                    RetailPOS.Core.Entities.Uom.PackStateOperations.TransferIn, StockLedgerReferenceType.StockTransfer, transfer.Id, actorId),
                cancellationToken);
        }

        await ledger.EnsureReconciledAsync(businessId, item.VariantId, transfer.ToLocationId, locationType, onHandAfter, cancellationToken);
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

    public static decimal ResolveRequestedQuantity(StockTransferCreateItem item)
    {
        if (item.RequestedQuantity.HasValue && item.RequestedQuantity.Value > 0)
            return item.RequestedQuantity.Value;
        if (item.Quantity > 0)
            return item.Quantity;
        if (item.TransferQuantity.HasValue && item.TransferQuantity.Value > 0)
            return item.TransferQuantity.Value;
        return item.Quantity;
    }

    public static decimal ResolveTransferQuantity(StockTransferCreateItem item)
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
