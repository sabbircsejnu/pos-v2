using RetailPOS.Core.Entities.Uom;

namespace RetailPOS.Application.Uom;

public sealed record ResolvedPurchaseLine(decimal Quantity, decimal UnitPrice, UnitTransactionSnapshot? Snapshot);

/// <summary>Physical make-up of an accepted receipt line.</summary>
public sealed record ReceiptPackBreakdown(int SealedPacks, decimal BaseQtyPerPack, string PackCode, decimal LooseQuantity);

public static class PurchaseUomRules
{
    /// <summary>
    /// Turns an entered PO line into base quantity and per-base price. The stored price stays exact to the cent over
    /// the line, so every existing quantity × price calculation, the GRN and the journal keep working unchanged.
    /// </summary>
    public static ResolvedPurchaseLine Resolve(
        decimal quantity,
        decimal unitPrice,
        long? purchaseOptionId,
        long? expectedConfigVersionId,
        ResolvedVariantUom? config)
    {
        if (purchaseOptionId is not null)
        {
            if (config is null)
                throw new UomValidationException(UomErrorCodes.OptionNotFound, "This item has no purchase units.");
            var line = UomLineResolver.ResolvePurchase(config, purchaseOptionId.Value, quantity, unitPrice, expectedConfigVersionId);
            return new ResolvedPurchaseLine(line.BaseQuantity, line.BaseUnitPrice, line.Snapshot);
        }

        if (config is null)
        {
            UomLineResolver.EnsureLegacyWholeQuantity(quantity, "Quantity");
            return new ResolvedPurchaseLine(quantity, unitPrice, null);
        }

        UomLineResolver.EnsureCurrentVersion(config, expectedConfigVersionId);
        var entry = UomLineResolver.ResolveStockEntry(config, null, quantity, expectedConfigVersionId);
        return new ResolvedPurchaseLine(entry.BaseQuantity, unitPrice, entry.Snapshot with { EnteredUnitPrice = unitPrice });
    }

    /// <summary>The purchase-unit cost of a GRN line as a per-base cost, using the PO's own conversion.</summary>
    public static decimal PerBaseCost(decimal enteredUnitCost, UnitTransactionSnapshot? snapshot) =>
        snapshot is null || snapshot.FactorNumerator == snapshot.FactorDenominator
            ? enteredUnitCost
            : UomQuantityMath.Normalize(Math.Round(enteredUnitCost * snapshot.FactorDenominator / snapshot.FactorNumerator, 12, MidpointRounding.AwayFromZero));

    /// <summary>
    /// Accepted stock split into sealed packs and loose. Without a stated split, a line bought in sealed packs is whole
    /// packs and any remainder is loose. A stated split must add up exactly (PUR-004, PUR-005).
    /// </summary>
    public static ReceiptPackBreakdown Breakdown(
        decimal accepted,
        int? statedSealedPacks,
        decimal? statedLoose,
        UnitTransactionSnapshot? snapshot,
        bool lineIsSealedPack)
    {
        var perPack = lineIsSealedPack && snapshot is not null
            ? UomQuantityMath.Normalize(snapshot.FactorNumerator / snapshot.FactorDenominator)
            : 0m;
        if (statedSealedPacks is null && statedLoose is null)
        {
            var packs = perPack > 0m ? (int)decimal.Floor(accepted / perPack) : 0;
            return new ReceiptPackBreakdown(packs, perPack, snapshot?.OptionCode ?? string.Empty, UomQuantityMath.Normalize(accepted - packs * perPack));
        }

        var sealedPacks = statedSealedPacks ?? 0;
        var loose = statedLoose ?? 0m;
        if (sealedPacks < 0 || loose < 0m)
            throw new UomValidationException(UomErrorCodes.InvalidPackBreakdown, "Sealed and loose quantities cannot be negative.");
        if (sealedPacks > 0 && perPack <= 0m)
            throw new UomValidationException(UomErrorCodes.InvalidPackBreakdown, "This line was not ordered in a sealed pack.");
        if (sealedPacks * perPack + loose != accepted)
            throw new UomValidationException(
                UomErrorCodes.QuantityMismatch,
                $"{sealedPacks} sealed × {perPack} + {loose} loose does not equal the accepted quantity of {accepted}.");
        return new ReceiptPackBreakdown(sealedPacks, perPack, snapshot?.OptionCode ?? string.Empty, UomQuantityMath.Normalize(loose));
    }

    public static async Task ApplyReceiptAsync(
        IPackStateLedger ledger,
        long businessId,
        long variantId,
        long locationId,
        string locationType,
        long grnId,
        long? actorUserId,
        ReceiptPackBreakdown breakdown,
        decimal onHandAfter,
        CancellationToken cancellationToken)
    {
        if (breakdown.SealedPacks > 0)
            await ledger.ApplyAsync(
                new PackStateChange(businessId, variantId, locationId, locationType, PackStates.Sealed, breakdown.PackCode, breakdown.BaseQtyPerPack,
                    breakdown.SealedPacks, breakdown.SealedPacks * breakdown.BaseQtyPerPack, 0m,
                    PackStateOperations.GrnReceipt, "grn", grnId, actorUserId),
                cancellationToken);
        if (breakdown.LooseQuantity > 0m)
            await ledger.ApplyAsync(
                new PackStateChange(businessId, variantId, locationId, locationType, PackStates.Loose, string.Empty, 0m,
                    0, breakdown.LooseQuantity, 0m,
                    PackStateOperations.GrnReceipt, "grn", grnId, actorUserId),
                cancellationToken);
        await ledger.EnsureReconciledAsync(businessId, variantId, locationId, locationType, onHandAfter, cancellationToken);
    }
}
