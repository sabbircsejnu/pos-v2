using Microsoft.Extensions.Logging;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.ReadStores;

public sealed class StockLedgerWriter(
    RetailPOSDbContext dbContext,
    ILogger<StockLedgerWriter> logger) : IStockLedgerWriter
{
    public void WriteEntry(
        long variantId,
        long locationId,
        string locationType,
        string transactionType,
        int qtyIn,
        int qtyOut,
        int balanceAfter,
        string referenceType,
        long referenceId,
        string? remarks = null,
        long? createdBy = null,
        DateTime? createdAt = null,
        decimal? unitCost = null,
        decimal? movementValue = null,
        DateTime? businessDate = null,
        long? movementSequence = null,
        long? reversalOfMovementId = null) =>
        WriteEntryInternal(
            variantId,
            locationId,
            locationType,
            transactionType,
            qtyIn,
            qtyOut,
            balanceAfter,
            referenceType,
            referenceId,
            remarks,
            createdBy,
            createdAt,
            unitCost,
            movementValue,
            businessDate,
            movementSequence,
            reversalOfMovementId);

    public void WriteEntryWithValuation(
        long variantId,
        long locationId,
        string locationType,
        string transactionType,
        int qtyIn,
        int qtyOut,
        int balanceAfter,
        string referenceType,
        long referenceId,
        string? remarks = null,
        long? createdBy = null,
        DateTime? createdAt = null,
        decimal? unitCost = null,
        decimal? movementValue = null,
        DateTime? businessDate = null,
        long? movementSequence = null,
        long? reversalOfMovementId = null) =>
        WriteEntryInternal(
            variantId,
            locationId,
            locationType,
            transactionType,
            qtyIn,
            qtyOut,
            balanceAfter,
            referenceType,
            referenceId,
            remarks,
            createdBy,
            createdAt,
            unitCost,
            movementValue,
            businessDate,
            movementSequence,
            reversalOfMovementId);

    private void WriteEntryInternal(
        long variantId,
        long locationId,
        string locationType,
        string transactionType,
        int qtyIn,
        int qtyOut,
        int balanceAfter,
        string referenceType,
        long referenceId,
        string? remarks,
        long? createdBy,
        DateTime? createdAt,
        decimal? unitCost,
        decimal? movementValue,
        DateTime? businessDate,
        long? movementSequence,
        long? reversalOfMovementId)
    {
        var normalizedLocationType = (locationType ?? string.Empty).Trim().ToLowerInvariant();
        var effectiveCreatedAt = createdAt ?? DateTime.UtcNow;
        var hasReliableCost = unitCost.HasValue || movementValue.HasValue;
        var resolvedUnitCost = unitCost ?? 0m;
        var resolvedMovementValue = movementValue
            ?? (hasReliableCost ? resolvedUnitCost * (qtyIn > 0 ? qtyIn : qtyOut) : 0m);
        var resolvedBusinessDate = businessDate ?? effectiveCreatedAt.Date;
        var resolvedMovementSequence = movementSequence
            ?? ResolveNextMovementSequence(variantId, locationId, normalizedLocationType);

        var entry = new StockLedger
        {
            VariantId = variantId,
            LocationId = locationId,
            LocationType = normalizedLocationType,
            TransactionType = transactionType,
            QtyIn = qtyIn,
            QtyOut = qtyOut,
            BalanceAfter = balanceAfter,
            UnitCost = resolvedUnitCost,
            MovementValue = resolvedMovementValue,
            IsCostValuationReliable = hasReliableCost,
            BusinessDate = resolvedBusinessDate,
            MovementSequence = resolvedMovementSequence,
            ReversalOfMovementId = reversalOfMovementId,
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            Remarks = remarks,
            CreatedBy = createdBy,
            CreatedAt = effectiveCreatedAt
        };

        dbContext.StockLedgers.Add(entry);

        logger.LogDebug(
            "StockLedger entry queued: variant={VariantId} loc={LocationId}/{LocationType} " +
            "type={TransactionType} in={QtyIn} out={QtyOut} balance={BalanceAfter} ref={RefType}#{RefId} unitCost={UnitCost} movementValue={MovementValue}",
            variantId,
            locationId,
            normalizedLocationType,
            transactionType,
            qtyIn,
            qtyOut,
            balanceAfter,
            referenceType,
            referenceId,
            entry.UnitCost,
            entry.MovementValue);
    }

    private long ResolveNextMovementSequence(long variantId, long locationId, string locationType)
    {
        var localMax = dbContext.StockLedgers.Local
            .Where(row => row.VariantId == variantId
                && row.LocationId == locationId
                && row.LocationType == locationType)
            .Select(row => row.MovementSequence)
            .DefaultIfEmpty(0)
            .Max();

        var dbMax = dbContext.StockLedgers
            .AsEnumerable()
            .Where(row => row.VariantId == variantId
                && row.LocationId == locationId
                && row.LocationType == locationType)
            .Select(row => row.MovementSequence)
            .DefaultIfEmpty(0)
            .Max();

        return Math.Max(localMax, dbMax) + 1;
    }
}
