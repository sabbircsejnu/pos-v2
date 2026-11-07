namespace RetailPOS.Application.Inventory.Costing;

/// <summary>The approved weighted-average pool of one variant at one location (P2-06).</summary>
public sealed class WeightedAverageCostPool
{
    public decimal Quantity { get; private set; }
    public decimal Value { get; private set; }

    public void Apply(decimal qtyIn, decimal qtyOut, decimal movementValue, decimal movementUnitCost)
    {
        if (qtyIn > 0)
        {
            Quantity += qtyIn;
            Value += movementValue;
        }

        if (qtyOut > 0)
        {
            var unitCost = Quantity == 0 ? movementUnitCost : Value / Quantity;
            Quantity -= qtyOut;
            Value -= Math.Round(unitCost * qtyOut, 2, MidpointRounding.AwayFromZero);
        }
    }
}

public enum StockCostKind
{
    /// <summary>The cost is unknown. Never shown or summed as zero.</summary>
    Unvalued,
    Valued,
    /// <summary>The stock really cost nothing, and stored documents prove it.</summary>
    VerifiedZero
}

public sealed record StockCostPosition(StockCostKind Kind, decimal? UnitCost, decimal? Value)
{
    public static readonly StockCostPosition Unvalued = new(StockCostKind.Unvalued, null, null);
}

public readonly record struct StockCostLocation(long LocationId, string LocationType);

public sealed record StockCostMovement(
    long VariantId,
    long LocationId,
    string LocationType,
    string ReferenceType,
    long ReferenceId,
    decimal QtyIn,
    decimal QtyOut,
    decimal UnitCost,
    decimal MovementValue,
    bool IsReliable);

/// <summary>The stored documents that can prove a zero cost.</summary>
public sealed record StockCostEvidence(
    IReadOnlySet<(long GrnId, long VariantId)> ZeroCostGrnLines,
    IReadOnlyDictionary<long, StockCostLocation> TransferOrigins)
{
    public static readonly StockCostEvidence None = new(
        new HashSet<(long, long)>(),
        new Dictionary<long, StockCostLocation>());
}

/// <summary>What one GRN line writes to the stock ledger.</summary>
public sealed record GrnLedgerCost(decimal? UnitCost, decimal? MovementValue, bool ZeroCostVerified)
{
    /// <summary>A receipt is free only when the documents agree: nothing was priced, or the order discounted it fully.</summary>
    public static bool IsZeroCostDocumented(decimal grnUnitCost, decimal orderUnitPrice, decimal orderDiscountPercent) =>
        (grnUnitCost == 0m && orderUnitPrice == 0m) || orderDiscountPercent >= 100m;

    /// <param name="journalValue">
    /// What this receipt debits to the Inventory account for the line, so the ledger and the journal stay equal.
    /// </param>
    public static GrnLedgerCost Resolve(
        decimal receivedQuantity,
        decimal grnUnitCost,
        decimal journalValue,
        decimal orderUnitPrice,
        decimal orderDiscountPercent)
    {
        if (receivedQuantity > 0 && journalValue > 0m)
            return new GrnLedgerCost(
                Math.Round(journalValue / receivedQuantity, 4, MidpointRounding.AwayFromZero),
                journalValue,
                false);

        return receivedQuantity > 0
            && journalValue == 0m
            && IsZeroCostDocumented(grnUnitCost, orderUnitPrice, orderDiscountPercent)
                ? new GrnLedgerCost(0m, 0m, true)
                : new GrnLedgerCost(null, null, false);
    }
}

/// <summary>
/// Replays stock movements into weighted-average pools and decides what each on-hand balance can honestly be worth.
/// A balance has a value only when reliable movements explain all of it, and a zero inbound cost must be proven by a
/// document: an old row can claim reliability while carrying a cost nobody ever supplied.
/// Feed movements in business date, sequence and id order.
/// </summary>
public sealed class StockCostLedger
{
    private sealed class State
    {
        public WeightedAverageCostPool Pool { get; } = new();
        public bool HasUnreliableQuantity { get; set; }
        public bool HasPositiveInbound { get; set; }
        public List<(string ReferenceType, long ReferenceId)> ZeroInbound { get; } = [];
    }

    private readonly Dictionary<(long VariantId, long LocationId, string LocationType), State> _states = new();
    private readonly HashSet<long> _zeroInboundGrnIds = [];
    private readonly HashSet<long> _zeroInboundTransferIds = [];

    public IReadOnlyCollection<long> ZeroInboundGrnIds => _zeroInboundGrnIds;
    public IReadOnlyCollection<long> ZeroInboundTransferIds => _zeroInboundTransferIds;

    public void Apply(StockCostMovement movement)
    {
        // Rejection and discrepancy evidence moves no stock, so it says nothing about value.
        if (movement.QtyIn == 0 && movement.QtyOut == 0)
            return;

        var key = (movement.VariantId, movement.LocationId, movement.LocationType);
        if (!_states.TryGetValue(key, out var state))
        {
            state = new State();
            _states[key] = state;
        }

        if (!movement.IsReliable)
        {
            state.HasUnreliableQuantity = true;
            return;
        }

        if (movement.QtyIn > 0)
        {
            if (movement.MovementValue > 0m)
            {
                state.HasPositiveInbound = true;
            }
            else
            {
                state.ZeroInbound.Add((movement.ReferenceType, movement.ReferenceId));
                if (movement.ReferenceType == StockCostReferenceTypes.Grn)
                    _zeroInboundGrnIds.Add(movement.ReferenceId);
                else if (movement.ReferenceType == StockCostReferenceTypes.StockTransfer)
                    _zeroInboundTransferIds.Add(movement.ReferenceId);
            }
        }

        state.Pool.Apply(movement.QtyIn, movement.QtyOut, movement.MovementValue, movement.UnitCost);
    }

    public StockCostPosition Classify(
        long variantId,
        StockCostLocation location,
        decimal onHand,
        StockCostEvidence evidence)
    {
        if (onHand <= 0 || !_states.TryGetValue((variantId, location.LocationId, location.LocationType), out var state))
            return StockCostPosition.Unvalued;

        if (state.Pool.Quantity != onHand || state.Pool.Value < 0m)
            return StockCostPosition.Unvalued;

        // One unproven zero would drag the average down, so it spoils the balance even when the total is positive.
        if (!AllZeroInboundProven(variantId, state, evidence, []))
            return StockCostPosition.Unvalued;

        if (state.Pool.Value > 0m)
        {
            return new StockCostPosition(
                StockCostKind.Valued,
                Math.Round(state.Pool.Value / onHand, 4, MidpointRounding.AwayFromZero),
                state.Pool.Value);
        }

        return state.ZeroInbound.Count > 0
            ? new StockCostPosition(StockCostKind.VerifiedZero, 0m, 0m)
            : StockCostPosition.Unvalued;
    }

    /// <summary>Whether everything this location ever received was free, as a zero-cost transfer out of it must be.</summary>
    public bool IsProvenFreeOrigin(long variantId, StockCostLocation location, StockCostEvidence evidence) =>
        IsProvenFreeOrigin(variantId, location, evidence, []);

    private bool IsProvenFreeOrigin(
        long variantId,
        StockCostLocation location,
        StockCostEvidence evidence,
        HashSet<StockCostLocation> visiting)
    {
        if (!visiting.Add(location)
            || !_states.TryGetValue((variantId, location.LocationId, location.LocationType), out var state)
            || state.HasUnreliableQuantity
            || state.HasPositiveInbound
            || state.ZeroInbound.Count == 0)
        {
            return false;
        }

        return AllZeroInboundProven(variantId, state, evidence, visiting);
    }

    private bool AllZeroInboundProven(
        long variantId,
        State state,
        StockCostEvidence evidence,
        HashSet<StockCostLocation> visiting)
    {
        foreach (var (referenceType, referenceId) in state.ZeroInbound)
        {
            var proven = referenceType switch
            {
                StockCostReferenceTypes.Grn => evidence.ZeroCostGrnLines.Contains((referenceId, variantId)),
                StockCostReferenceTypes.StockTransfer =>
                    evidence.TransferOrigins.TryGetValue(referenceId, out var origin)
                    && IsProvenFreeOrigin(variantId, origin, evidence, visiting),
                // A reversed repack returns stock that was proven free when the job consumed it.
                Core.Entities.UomStockLedgerTypes.RepackReference => true,
                _ => false
            };
            if (!proven)
                return false;
        }

        return true;
    }
}

public static class StockCostReferenceTypes
{
    public const string Grn = Core.Entities.StockLedgerReferenceType.Grn;
    public const string StockTransfer = Core.Entities.StockLedgerReferenceType.StockTransfer;
}
