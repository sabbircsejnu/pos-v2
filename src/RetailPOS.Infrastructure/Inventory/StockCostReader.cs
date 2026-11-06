using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Inventory.Costing;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.ReadStores;

/// <summary>Loads the movements and the documents that the shared stock cost rules are decided from.</summary>
internal static class StockCostReader
{
    /// <summary>Corrections recorded for this Business, optionally for one variant.</summary>
    public static async Task<StockCostCorrectionSet> ReadCorrectionsAsync(
        RetailPOSDbContext dbContext,
        long businessId,
        long? variantId,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.StockLedgers
            .AsNoTracking()
            .Where(row => row.ReferenceType == StockLedgerReferenceType.StockCostCorrection
                && row.Variant.BusinessId == businessId
                && (variantId == null || row.VariantId == variantId))
            .OrderBy(row => row.Id)
            .Select(row => new { row.ReferenceId, row.UnitCost, row.MovementValue })
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
            return StockCostCorrectionSet.Empty;

        var corrections = new Dictionary<long, (decimal, decimal)>();
        foreach (var row in rows)
            corrections[row.ReferenceId] = (row.UnitCost, row.MovementValue);
        return new StockCostCorrectionSet(corrections);
    }

    /// <summary>The whole history of one variant in this Business, because a zero cost can only be proven along its chain.</summary>
    public static async Task<StockCostLedger> ReadVariantAsync(
        RetailPOSDbContext dbContext,
        long businessId,
        long variantId,
        CancellationToken cancellationToken)
    {
        var corrections = await ReadCorrectionsAsync(dbContext, businessId, variantId, cancellationToken);
        var movements = await dbContext.StockLedgers
            .AsNoTracking()
            .Where(movement => movement.VariantId == variantId && movement.Variant.BusinessId == businessId)
            .OrderBy(movement => movement.BusinessDate)
            .ThenBy(movement => movement.MovementSequence)
            .ThenBy(movement => movement.Id)
            .Select(movement => new
            {
                movement.Id,
                movement.VariantId,
                movement.LocationId,
                movement.LocationType,
                movement.ReferenceType,
                movement.ReferenceId,
                movement.QtyIn,
                movement.QtyOut,
                movement.UnitCost,
                movement.MovementValue,
                movement.IsCostValuationReliable
            })
            .ToListAsync(cancellationToken);

        var ledger = new StockCostLedger();
        foreach (var movement in movements)
        {
            var (unitCost, value, isReliable) = corrections.Resolve(
                movement.Id,
                movement.UnitCost,
                movement.MovementValue,
                movement.IsCostValuationReliable);
            ledger.Apply(new StockCostMovement(
                movement.VariantId,
                movement.LocationId,
                movement.LocationType,
                movement.ReferenceType,
                movement.ReferenceId,
                movement.QtyIn,
                movement.QtyOut,
                unitCost,
                value,
                isReliable));
        }

        return ledger;
    }

    public static async Task<StockCostEvidence> ReadEvidenceAsync(
        RetailPOSDbContext dbContext,
        StockCostLedger ledger,
        CancellationToken cancellationToken)
    {
        if (ledger.ZeroInboundGrnIds.Count == 0 && ledger.ZeroInboundTransferIds.Count == 0)
            return StockCostEvidence.None;

        var grnIds = ledger.ZeroInboundGrnIds.ToList();
        var grnLines = await dbContext.GrnItems
            .AsNoTracking()
            .Where(item => grnIds.Contains(item.GrnId) && item.Grn.Status == "completed")
            .Select(item => new
            {
                item.GrnId,
                item.PurchaseOrderItem.VariantId,
                item.UnitCost,
                item.PurchaseOrderItem.UnitPrice,
                item.PurchaseOrderItem.Discount
            })
            .ToListAsync(cancellationToken);

        return new StockCostEvidence(
            grnLines
                .Where(line => GrnLedgerCost.IsZeroCostDocumented(line.UnitCost, line.UnitPrice, line.Discount))
                .Select(line => (line.GrnId, line.VariantId))
                .ToHashSet(),
            await ReadTransferOriginsAsync(dbContext, ledger.ZeroInboundTransferIds.ToList(), cancellationToken));
    }

    /// <summary>A system return never left its own source, so the cost it carries began at the original transfer's source.</summary>
    private static async Task<Dictionary<long, StockCostLocation>> ReadTransferOriginsAsync(
        RetailPOSDbContext dbContext,
        List<long> transferIds,
        CancellationToken cancellationToken)
    {
        if (transferIds.Count == 0)
            return [];

        var transfers = await dbContext.StockTransfers
            .AsNoTracking()
            .Where(transfer => transferIds.Contains(transfer.Id))
            .Select(transfer => new
            {
                transfer.Id,
                transfer.FromLocationId,
                transfer.FromLocationType,
                transfer.OriginalTransferId
            })
            .ToListAsync(cancellationToken);

        var originalIds = transfers
            .Where(transfer => transfer.OriginalTransferId.HasValue)
            .Select(transfer => transfer.OriginalTransferId!.Value)
            .Distinct()
            .ToList();
        var originals = await dbContext.StockTransfers
            .AsNoTracking()
            .Where(transfer => originalIds.Contains(transfer.Id))
            .Select(transfer => new { transfer.Id, transfer.FromLocationId, transfer.FromLocationType })
            .ToDictionaryAsync(transfer => transfer.Id, cancellationToken);

        var origins = new Dictionary<long, StockCostLocation>();
        foreach (var transfer in transfers)
        {
            if (transfer.OriginalTransferId is not { } originalId)
            {
                origins[transfer.Id] = new StockCostLocation(
                    transfer.FromLocationId,
                    transfer.FromLocationType.Trim().ToLowerInvariant());
            }
            else if (originals.TryGetValue(originalId, out var original))
            {
                origins[transfer.Id] = new StockCostLocation(
                    original.FromLocationId,
                    original.FromLocationType.Trim().ToLowerInvariant());
            }
        }

        return origins;
    }
}
