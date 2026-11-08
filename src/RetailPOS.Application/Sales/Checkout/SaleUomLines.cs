using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Application.Sales.Commands;
using RetailPOS.Application.Sales.FinancialPostings;
using RetailPOS.Application.Uom;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Uom;

namespace RetailPOS.Application.Sales.Checkout;

public sealed record SalePhysicalForm(string State, string PackCode, decimal BaseQtyPerPack, int PackCount);

public sealed record SalePackagingNeed(long VariantId, decimal Quantity);

/// <summary>A checkout line after the server has interpreted its unit. Legacy pcs lines have no resolution.</summary>
public sealed record SaleUomLine(
    SaleLineResolution? Resolution,
    decimal BaseQuantity,
    SalePhysicalForm? Form,
    IReadOnlyList<SalePackagingNeed> Packaging);

public static class SaleUomLines
{
    public static SaleUomLine Resolve(CreateSaleLine item, ResolvedVariantUom? config)
    {
        if (item.SellingOptionId is null)
        {
            var needsOption = config is not null
                && (config.PackStateTrackingEnabled || !string.Equals(config.BaseUnit.Code, UomUnit.PcsCode, StringComparison.OrdinalIgnoreCase));
            if (needsOption)
                throw new UomValidationException(
                    UomErrorCodes.OptionRequired,
                    $"Variant ID {item.VariantId} is sold in configured units. Choose a pack or a custom quantity.");
            UomLineResolver.EnsureLegacyWholeQuantity(item.Quantity, $"Quantity for variant ID {item.VariantId}");
            return new SaleUomLine(null, item.Quantity, null, []);
        }

        if (config is null)
            throw new UomValidationException(UomErrorCodes.OptionNotFound, $"Variant ID {item.VariantId} has no selling options.");

        var resolution = UomLineResolver.ResolveSale(config, item.SellingOptionId.Value, item.Quantity, item.ExpectedConfigVersionId);
        var form = ResolveForm(item, config, resolution);
        return new SaleUomLine(resolution, resolution.BaseQuantity, form, ResolvePackaging(item, resolution, form));
    }

    /// <summary>Applies the rule the pricing engine chose. Without one the line keeps its option price exactly.</summary>
    public static SaleUomLine WithAppliedRule(SaleUomLine line, RetailPOS.Application.Pos.ReadModels.PosCartPriceReadModel priced)
    {
        if (line.Resolution is not { } resolution || priced.LineGross is not { } gross || gross == resolution.LineGross)
            return line;
        var entered = resolution.Snapshot.EnteredQuantity;
        return line with
        {
            Resolution = resolution with
            {
                LineGross = gross,
                BaseUnitPrice = UomQuantityMath.PerBaseAmount(gross, resolution.BaseQuantity),
                Snapshot = resolution.Snapshot with { EnteredUnitPrice = UomQuantityMath.PerBaseAmount(gross, entered) }
            }
        };
    }

    private static SalePhysicalForm? ResolveForm(CreateSaleLine item, ResolvedVariantUom config, SaleLineResolution resolution)
    {
        var option = resolution.Option;
        if (!config.PackStateTrackingEnabled)
        {
            if (!string.IsNullOrWhiteSpace(item.FulfilmentState))
                throw new UomValidationException(UomErrorCodes.PackStateNotEnabled, "This item does not track sealed and loose stock.");
            return null;
        }

        var state = string.IsNullOrWhiteSpace(item.FulfilmentState)
            ? option.SealedPackCode is not null ? PackStates.Sealed : PackStates.Loose
            : item.FulfilmentState.Trim().ToLowerInvariant();
        switch (state)
        {
            case PackStates.Loose:
                // A pack mapped to sealed stock is that physical pack. It is never quietly filled from loose stock (STATE-005).
                if (option.SealedPackCode is not null)
                    throw new UomValidationException(UomErrorCodes.PackStateMismatch, $"'{option.Label}' is sold as a sealed pack.");
                return new SalePhysicalForm(PackStates.Loose, string.Empty, 0m, 0);
            case PackStates.Sealed:
                if (option.SealedPackCode is null)
                    throw new UomValidationException(UomErrorCodes.PackStateMismatch, $"'{option.Label}' is not sold from sealed stock.");
                return new SalePhysicalForm(PackStates.Sealed, option.SealedPackCode, option.BaseQuantity!.Value, (int)item.Quantity);
            case PackStates.Prepared:
                if (!option.IsPreparedEligible)
                    throw new UomValidationException(UomErrorCodes.PackStateMismatch, $"'{option.Label}' has no prepared stock.");
                return new SalePhysicalForm(PackStates.Prepared, option.Code, option.BaseQuantity!.Value, (int)item.Quantity);
            default:
                throw new UomValidationException(UomErrorCodes.PackStateMismatch, $"'{item.FulfilmentState}' is not a stock form.");
        }
    }

    /// <summary>
    /// Only stock filled at the counter uses new packaging. A sealed or prepared pack already is its packaging, so taking
    /// a bottle for it would count that bottle twice (POS-007). A custom sale uses one set per line, not per millilitre.
    /// </summary>
    private static IReadOnlyList<SalePackagingNeed> ResolvePackaging(CreateSaleLine item, SaleLineResolution resolution, SalePhysicalForm? form)
    {
        if (form is not null && form.State != PackStates.Loose)
            return [];
        var multiplier = resolution.Option.Kind == SellingOptionKinds.Pack ? item.Quantity : 1m;
        return resolution.Option.Components
            .Where(component => component.IsRequired || item.IncludePackaging)
            .Select(component => new SalePackagingNeed(component.VariantId, UomQuantityMath.Normalize(component.Quantity * multiplier)))
            .ToList();
    }
}

/// <summary>Puts a sale line's physical stock back, exactly as it left, for a void or an eligible return.</summary>
public static class SaleUomReversal
{
    /// <returns>The packaging value returned to inventory.</returns>
    public static async Task<decimal> RestoreAsync(
        Sale sale,
        SaleItem item,
        decimal quantity,
        bool packagingRestored,
        long actorUserId,
        IPackStateLedger? packState,
        IStockLedgerWriter? stockLedger,
        Func<long, CancellationToken, Task<Core.Entities.Inventory?>> findInventory,
        CancellationToken cancellationToken,
        bool variantTracksPackState = false,
        decimal alreadyReturned = 0m,
        long? locationId = null,
        string? referenceType = null,
        long? referenceId = null,
        string? ledgerTransactionType = null)
    {
        // Defaults keep refunds and voids exactly as before; an exchange restores at its receiving outlet under its own reference.
        var location = locationId ?? sale.OutletId;
        var reference = referenceType ?? StockLedgerReferenceType.Sale;
        var referenceKey = referenceId ?? sale.Id;
        var ledgerType = ledgerTransactionType ?? StockLedgerTransactionType.Return;
        var restoredValue = 0m;
        var businessId = sale.BusinessId ?? 0;
        if (item.PackState is not null && packState is not null && variantTracksPackState)
        {
            var physicalPack = item.PackState != PackStates.Loose;
            var perPack = item.BaseQtyPerPack ?? 0m;
            var wholePacks = physicalPack && perPack > 0m && quantity % perPack == 0m;
            if (physicalPack && packagingRestored && wholePacks)
            {
                var packs = (int)(quantity / perPack);
                var share = item.PackState == PackStates.Prepared
                    ? SaleFinancialIntentFactory.ShareOfReturn(item.PackagingCost, alreadyReturned, quantity, item.Quantity)
                    : 0m;
                await packState.ApplyAsync(
                    new PackStateChange(businessId, item.VariantId, location, "outlet", item.PackState, item.PackCode ?? string.Empty, perPack,
                        packs, quantity, share, PackStateOperations.SaleReversal, reference, referenceKey, actorUserId),
                    cancellationToken);
                restoredValue += share;
            }
            else
            {
                await packState.ApplyAsync(
                    new PackStateChange(businessId, item.VariantId, location, "outlet", PackStates.Loose, string.Empty, 0m,
                        0, quantity, 0m, PackStateOperations.SaleReversal, reference, referenceKey, actorUserId),
                    cancellationToken);
            }
        }

        else if (variantTracksPackState && packState is not null)
        {
            // Sold before tracking was switched on: it has no recorded form, so it returns as loose stock.
            await packState.ApplyAsync(
                new PackStateChange(businessId, item.VariantId, location, "outlet", PackStates.Loose, string.Empty, 0m,
                    0, quantity, 0m, PackStateOperations.SaleReversal, reference, referenceKey, actorUserId),
                cancellationToken);
        }

        // Packaging comes back only when someone confirmed it can be used again, and only in whole items: half a bottle
        // cannot go back on a shelf, so a return that does not divide evenly leaves that packaging's cost expensed.
        if (packagingRestored && stockLedger is not null && item.Quantity > 0m)
        {
            foreach (var component in item.Components)
            {
                var restoredQuantity = UomQuantityMath.Normalize(component.Quantity * quantity / item.Quantity);
                if (restoredQuantity % 1m != 0m)
                    continue;
                var inventory = await findInventory(component.ComponentVariantId, cancellationToken);
                if (inventory is null)
                    continue;
                var value = SaleFinancialIntentFactory.ShareOfReturn(component.Value, alreadyReturned, quantity, item.Quantity);
                inventory.Quantity += restoredQuantity;
                stockLedger.WriteEntryWithValuation(
                    variantId: component.ComponentVariantId,
                    locationId: location,
                    locationType: "outlet",
                    transactionType: ledgerType,
                    qtyIn: restoredQuantity,
                    qtyOut: 0m,
                    balanceAfter: inventory.Quantity,
                    referenceType: reference,
                    referenceId: referenceKey,
                    remarks: $"Packaging restored from sale line {item.Id}",
                    createdBy: actorUserId,
                    unitCost: component.UnitCost,
                    movementValue: value);
                restoredValue += value;
            }
        }

        return restoredValue;
    }
}
