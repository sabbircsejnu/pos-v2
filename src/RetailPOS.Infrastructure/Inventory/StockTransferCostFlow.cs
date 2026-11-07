using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.Costing;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.ReadStores;

namespace RetailPOS.Infrastructure.StockTransfers;

internal sealed record StockTransferCost(StockCostKind Kind, decimal UnitCost, decimal Value)
{
    public static readonly StockTransferCost Unvalued = new(StockCostKind.Unvalued, 0m, 0m);
}

/// <summary>
/// Stock moves between locations at its source weighted-average cost, which only the server decides. The dispatch
/// ledger row is the record of what left the source, so receipts and returns are valued from it and never from a
/// client value. A transfer posts no journal, so it cannot create gain, loss or COGS.
/// </summary>
internal static class StockTransferCostFlow
{
    public static async Task<StockTransferCost> ResolveDispatchCostAsync(
        RetailPOSDbContext dbContext,
        long businessId,
        long variantId,
        long locationId,
        string locationType,
        decimal onHandBeforeDispatch,
        decimal quantity,
        CancellationToken cancellationToken)
    {
        var ledger = await StockCostReader.ReadVariantAsync(dbContext, businessId, variantId, cancellationToken);
        var evidence = await StockCostReader.ReadEvidenceAsync(dbContext, ledger, cancellationToken);
        var position = ledger.Classify(
            variantId,
            new StockCostLocation(locationId, locationType),
            onHandBeforeDispatch,
            evidence);

        return position.Kind switch
        {
            StockCostKind.Valued => new StockTransferCost(
                StockCostKind.Valued,
                position.UnitCost!.Value,
                // The same rounding the pool applies when it takes these units out, so the source stays exact.
                Math.Round(position.Value!.Value / onHandBeforeDispatch * quantity, 2, MidpointRounding.AwayFromZero)),
            StockCostKind.VerifiedZero => new StockTransferCost(StockCostKind.VerifiedZero, 0m, 0m),
            _ => StockTransferCost.Unvalued
        };
    }

    /// <summary>The value of the units accepted here, as a share of what the dispatch row took out of the source.</summary>
    public static async Task<StockTransferCost> ResolveReceiptCostAsync(
        RetailPOSDbContext dbContext,
        long businessId,
        StockTransfer transfer,
        StockTransferItem item,
        decimal acceptedQuantity,
        CancellationToken cancellationToken)
    {
        var dispatchTransferId = transfer.OriginalTransferId ?? transfer.Id;
        var dispatched = await dbContext.StockLedgers
            .AsNoTracking()
            .Where(movement =>
                movement.ReferenceType == StockLedgerReferenceType.StockTransfer
                && movement.ReferenceId == dispatchTransferId
                && movement.VariantId == item.VariantId
                && movement.QtyOut > 0)
            .Select(movement => new
            {
                movement.Id,
                movement.LocationId,
                movement.LocationType,
                movement.QtyOut,
                movement.UnitCost,
                movement.MovementValue,
                movement.IsCostValuationReliable
            })
            .ToListAsync(cancellationToken);
        var corrections = await StockCostReader.ReadCorrectionsAsync(dbContext, businessId, item.VariantId, cancellationToken);
        var resolved = dispatched
            .Select(row => corrections.Resolve(row.Id, row.UnitCost, row.MovementValue, row.IsCostValuationReliable))
            .ToList();
        if (dispatched.Count == 0 || resolved.Any(row => !row.IsReliable))
            return StockTransferCost.Unvalued;

        var dispatchedQuantity = dispatched.Sum(row => row.QtyOut);
        var dispatchedValue = resolved.Sum(row => row.Value);
        if (dispatchedValue <= 0m)
        {
            // A transfer dispatched before this rule may say "reliable" at zero. It must still prove the zero.
            var ledger = await StockCostReader.ReadVariantAsync(dbContext, businessId, item.VariantId, cancellationToken);
            var evidence = await StockCostReader.ReadEvidenceAsync(dbContext, ledger, cancellationToken);
            var origin = new StockCostLocation(dispatched[0].LocationId, dispatched[0].LocationType);
            return ledger.IsProvenFreeOrigin(item.VariantId, origin, evidence)
                ? new StockTransferCost(StockCostKind.VerifiedZero, 0m, 0m)
                : StockTransferCost.Unvalued;
        }

        var value = transfer.OriginalTransferId is null
            ? StockTransferValue.Share(dispatchedValue, acceptedQuantity, dispatchedQuantity)
            : await ReturnValueAsync(
                dbContext,
                dispatchTransferId,
                item.VariantId,
                dispatchedValue,
                dispatchedQuantity,
                acceptedQuantity,
                cancellationToken);
        if (value is null)
            return StockTransferCost.Unvalued;

        return new StockTransferCost(
            StockCostKind.Valued,
            Math.Round(dispatchedValue / dispatchedQuantity, 4, MidpointRounding.AwayFromZero),
            value.Value);
    }

    public static void Write(
        IStockLedgerWriter ledger,
        StockTransferCost cost,
        long variantId,
        long locationId,
        string locationType,
        string transactionType,
        decimal qtyIn,
        decimal qtyOut,
        decimal balanceAfter,
        long transferId,
        string remarks,
        long? createdBy,
        DateTime? createdAt = null)
    {
        switch (cost.Kind)
        {
            case StockCostKind.Valued:
                ledger.WriteEntryWithValuation(
                    variantId: variantId,
                    locationId: locationId,
                    locationType: locationType,
                    transactionType: transactionType,
                    qtyIn: qtyIn,
                    qtyOut: qtyOut,
                    balanceAfter: balanceAfter,
                    referenceType: StockLedgerReferenceType.StockTransfer,
                    referenceId: transferId,
                    remarks: remarks,
                    createdBy: createdBy,
                    createdAt: createdAt,
                    unitCost: cost.UnitCost,
                    movementValue: cost.Value);
                break;
            case StockCostKind.VerifiedZero:
                ledger.WriteVerifiedZeroCostEntry(
                    variantId,
                    locationId,
                    locationType,
                    transactionType,
                    qtyIn,
                    qtyOut,
                    balanceAfter,
                    StockLedgerReferenceType.StockTransfer,
                    transferId,
                    remarks,
                    createdBy,
                    createdAt);
                break;
            default:
                // No cost is supplied, so the writer records the movement as unreliable.
                ledger.WriteEntryWithValuation(
                    variantId: variantId,
                    locationId: locationId,
                    locationType: locationType,
                    transactionType: transactionType,
                    qtyIn: qtyIn,
                    qtyOut: qtyOut,
                    balanceAfter: balanceAfter,
                    referenceType: StockLedgerReferenceType.StockTransfer,
                    referenceId: transferId,
                    remarks: remarks,
                    createdBy: createdBy,
                    createdAt: createdAt);
                break;
        }
    }

    /// <summary>The rejected share is what remains after the accepted and lost shares, so the original value is fully accounted for.</summary>
    private static async Task<decimal?> ReturnValueAsync(
        RetailPOSDbContext dbContext,
        long originalTransferId,
        long variantId,
        decimal dispatchedValue,
        decimal dispatchedQuantity,
        decimal acceptedQuantity,
        CancellationToken cancellationToken)
    {
        var original = await dbContext.StockTransferItems
            .AsNoTracking()
            .Where(line => line.TransferId == originalTransferId && line.VariantId == variantId)
            .Select(line => new
            {
                line.AcceptedQuantity,
                line.RejectedQuantity,
                line.MissingQuantity,
                line.DamagedQuantity
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (original is null || original.RejectedQuantity <= 0)
            return null;

        return StockTransferValue.Share(
            StockTransferValue.RejectedValue(
                dispatchedValue,
                dispatchedQuantity,
                original.AcceptedQuantity,
                original.MissingQuantity + original.DamagedQuantity),
            acceptedQuantity,
            original.RejectedQuantity);
    }
}
