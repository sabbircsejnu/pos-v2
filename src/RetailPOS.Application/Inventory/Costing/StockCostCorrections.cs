using RetailPOS.Core.Entities;

namespace RetailPOS.Application.Inventory.Costing;

/// <summary>
/// Approved corrections to the cost of earlier movements. Ledger rows are immutable, so a correction is its own row and
/// valuation applies it at the original movement's place in history. With no corrections, nothing changes.
/// </summary>
public sealed class StockCostCorrectionSet(IReadOnlyDictionary<long, (decimal UnitCost, decimal Value)> corrections)
{
    public static readonly StockCostCorrectionSet Empty = new(new Dictionary<long, (decimal, decimal)>());

    public int Count => corrections.Count;

    public (decimal UnitCost, decimal Value, bool IsReliable) Resolve(
        long movementId,
        decimal unitCost,
        decimal value,
        bool isReliable) =>
        corrections.TryGetValue(movementId, out var corrected)
            ? (corrected.UnitCost, corrected.Value, true)
            : (unitCost, value, isReliable);
}

/// <summary>How a transfer's dispatched value divides across what happened to its units.</summary>
public static class StockTransferValue
{
    public static decimal Share(decimal value, int quantity, int ofQuantity) =>
        quantity == ofQuantity
            ? value
            : Math.Round(value * quantity / ofQuantity, 2, MidpointRounding.AwayFromZero);

    /// <summary>What remains after the accepted and lost shares, so the dispatched value is fully accounted for.</summary>
    public static decimal RejectedValue(decimal dispatchedValue, int dispatchedQuantity, int accepted, int lost) =>
        dispatchedValue
        - Share(dispatchedValue, accepted, dispatchedQuantity)
        - Share(dispatchedValue, lost, dispatchedQuantity);
}

public sealed record StockCostPlanMovement(
    long Id,
    long VariantId,
    long LocationId,
    string LocationType,
    string TransactionType,
    string ReferenceType,
    long ReferenceId,
    int QtyIn,
    int QtyOut,
    int BalanceAfter,
    decimal UnitCost,
    decimal MovementValue,
    bool IsReliable);

public sealed record GrnCostLine(
    long GrnId,
    long VariantId,
    int ReceivedQuantity,
    decimal UnitCost,
    decimal? TaxableAmount,
    decimal OrderUnitPrice,
    decimal OrderDiscountPercent);

public sealed record TransferCostLine(
    long TransferId,
    long VariantId,
    long? OriginalTransferId,
    int AcceptedQuantity,
    int RejectedQuantity,
    int MissingQuantity,
    int DamagedQuantity);

public sealed record PlannedStockCostCorrection(StockCostPlanMovement Movement, decimal UnitCost, decimal Value, string Basis);

public sealed record SkippedStockCostCorrection(StockCostPlanMovement Movement, string Reason);

public sealed record StockCostCorrectionPlan(
    IReadOnlyList<PlannedStockCostCorrection> Corrections,
    IReadOnlyList<SkippedStockCostCorrection> Skipped);

/// <summary>
/// Decides which uncosted receipts, and transfers that came from them, can be corrected from stored evidence. A receipt
/// is corrected only when its GRN agrees with the journal that was posted for it. A transfer takes the average its
/// source held at that moment, and only when reliable history explains that source's whole balance. Nothing is
/// estimated, and Product Master cost is never used.
/// </summary>
public static class StockCostCorrectionPlanner
{
    /// <param name="movements">Every movement of the Business in the order it was written, so a dispatch precedes its receipt.</param>
    public static StockCostCorrectionPlan Build(
        IReadOnlyList<StockCostPlanMovement> movements,
        IReadOnlyList<GrnCostLine> grnLines,
        IReadOnlyDictionary<long, decimal> journalDebitsByGrn,
        IReadOnlyList<TransferCostLine> transferLines,
        StockCostCorrectionSet existing)
    {
        var corrections = new List<PlannedStockCostCorrection>();
        var skipped = new List<SkippedStockCostCorrection>();
        var pools = new Dictionary<(long, long, string), WeightedAverageCostPool>();
        var dispatched = new Dictionary<(long TransferId, long VariantId), (int Quantity, decimal Value)>();
        var grnValues = GrnValues(grnLines, journalDebitsByGrn);
        var linesByGrn = grnLines.ToLookup(line => (line.GrnId, line.VariantId));
        var transfers = transferLines.ToDictionary(line => (line.TransferId, line.VariantId));

        foreach (var movement in movements)
        {
            if (movement.QtyIn == 0 && movement.QtyOut == 0)
                continue;

            var (unitCost, value, isReliable) = existing.Resolve(
                movement.Id,
                movement.UnitCost,
                movement.MovementValue,
                movement.IsReliable);
            var key = (movement.VariantId, movement.LocationId, movement.LocationType);
            if (!pools.TryGetValue(key, out var pool))
            {
                pool = new WeightedAverageCostPool();
                pools[key] = pool;
            }

            if (!isReliable || value <= 0m)
            {
                var resolved = movement.ReferenceType switch
                {
                    StockLedgerReferenceType.Grn when movement.QtyIn > 0 => FromGrn(movement, isReliable, linesByGrn, grnValues, skipped),
                    StockLedgerReferenceType.StockTransfer when movement.QtyOut > 0 => FromSourcePool(movement, pool, skipped),
                    StockLedgerReferenceType.StockTransfer when movement.QtyIn > 0 => FromDispatch(movement, dispatched, transfers, skipped),
                    _ => null
                };
                if (resolved is not null)
                {
                    corrections.Add(resolved);
                    (unitCost, value, isReliable) = (resolved.UnitCost, resolved.Value, true);
                }
            }

            if (movement.ReferenceType == StockLedgerReferenceType.StockTransfer
                && movement.QtyOut > 0
                && isReliable
                && value > 0m)
            {
                dispatched[(movement.ReferenceId, movement.VariantId)] = (movement.QtyOut, value);
            }

            if (isReliable)
                pool.Apply(movement.QtyIn, movement.QtyOut, value, unitCost);
        }

        return new StockCostCorrectionPlan(corrections, skipped);
    }

    /// <summary>The two receiving paths debit Inventory differently, so the posted journal says which value is right.</summary>
    private static Dictionary<long, Func<GrnCostLine, decimal>?> GrnValues(
        IReadOnlyList<GrnCostLine> grnLines,
        IReadOnlyDictionary<long, decimal> journalDebitsByGrn)
    {
        decimal Taxable(GrnCostLine line) => line.TaxableAmount ?? line.ReceivedQuantity * line.UnitCost;
        decimal Gross(GrnCostLine line) => line.ReceivedQuantity * line.UnitCost;

        var result = new Dictionary<long, Func<GrnCostLine, decimal>?>();
        foreach (var grn in grnLines.GroupBy(line => line.GrnId))
        {
            var debit = journalDebitsByGrn.GetValueOrDefault(grn.Key);
            result[grn.Key] = Math.Round(grn.Sum(Taxable), 2, MidpointRounding.AwayFromZero) == debit
                ? Taxable
                : Math.Round(grn.Sum(Gross), 2, MidpointRounding.AwayFromZero) == debit
                    ? Gross
                    : null;
        }

        return result;
    }

    private static PlannedStockCostCorrection? FromGrn(
        StockCostPlanMovement movement,
        bool isReliable,
        ILookup<(long, long), GrnCostLine> linesByGrn,
        Dictionary<long, Func<GrnCostLine, decimal>?> grnValues,
        List<SkippedStockCostCorrection> skipped)
    {
        var lines = linesByGrn[(movement.ReferenceId, movement.VariantId)]
            .Where(line => line.ReceivedQuantity == movement.QtyIn)
            .ToList();
        if (lines.Count != 1)
        {
            skipped.Add(new(movement, "No single GRN line matches this receipt"));
            return null;
        }

        if (grnValues.GetValueOrDefault(movement.ReferenceId) is not { } valueOf)
        {
            skipped.Add(new(movement, "The GRN does not agree with its posted journal"));
            return null;
        }

        var line = lines[0];
        var cost = GrnLedgerCost.Resolve(
            line.ReceivedQuantity,
            line.UnitCost,
            valueOf(line),
            line.OrderUnitPrice,
            line.OrderDiscountPercent);
        if (cost.UnitCost is null || cost.MovementValue is null)
        {
            skipped.Add(new(movement, "The GRN has no cost and its documents do not prove the receipt was free"));
            return null;
        }

        // An already reliable row at a proven zero needs nothing.
        return isReliable && cost.ZeroCostVerified
            ? null
            : new PlannedStockCostCorrection(
                movement,
                cost.UnitCost.Value,
                cost.MovementValue.Value,
                $"GRN {movement.ReferenceId} cost");
    }

    private static PlannedStockCostCorrection? FromSourcePool(
        StockCostPlanMovement movement,
        WeightedAverageCostPool pool,
        List<SkippedStockCostCorrection> skipped)
    {
        if (pool.Quantity != movement.BalanceAfter + movement.QtyOut || pool.Value <= 0m)
        {
            skipped.Add(new(movement, "Reliable history does not explain the source balance at dispatch"));
            return null;
        }

        var average = pool.Value / pool.Quantity;
        return new PlannedStockCostCorrection(
            movement,
            Math.Round(average, 4, MidpointRounding.AwayFromZero),
            Math.Round(average * movement.QtyOut, 2, MidpointRounding.AwayFromZero),
            $"Source average at dispatch of transfer {movement.ReferenceId}");
    }

    private static PlannedStockCostCorrection? FromDispatch(
        StockCostPlanMovement movement,
        Dictionary<(long, long), (int Quantity, decimal Value)> dispatched,
        Dictionary<(long, long), TransferCostLine> transfers,
        List<SkippedStockCostCorrection> skipped)
    {
        transfers.TryGetValue((movement.ReferenceId, movement.VariantId), out var line);
        var dispatchTransferId = line?.OriginalTransferId ?? movement.ReferenceId;
        if (!dispatched.TryGetValue((dispatchTransferId, movement.VariantId), out var source))
        {
            skipped.Add(new(movement, "The dispatch of this transfer has no reliable value"));
            return null;
        }

        decimal value;
        if (line?.OriginalTransferId is null)
        {
            value = StockTransferValue.Share(source.Value, movement.QtyIn, source.Quantity);
        }
        else
        {
            if (!transfers.TryGetValue((dispatchTransferId, movement.VariantId), out var original)
                || original.RejectedQuantity <= 0)
            {
                skipped.Add(new(movement, "The original transfer of this return has no rejected quantity"));
                return null;
            }

            value = StockTransferValue.Share(
                StockTransferValue.RejectedValue(
                    source.Value,
                    source.Quantity,
                    original.AcceptedQuantity,
                    original.MissingQuantity + original.DamagedQuantity),
                movement.QtyIn,
                original.RejectedQuantity);
        }

        return new PlannedStockCostCorrection(
            movement,
            Math.Round(source.Value / source.Quantity, 4, MidpointRounding.AwayFromZero),
            value,
            $"Dispatch value of transfer {dispatchTransferId}");
    }
}
