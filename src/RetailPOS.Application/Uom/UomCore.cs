using RetailPOS.Core.Entities.Uom;
using RetailPOS.Core.Money;

namespace RetailPOS.Application.Uom;

public static class UomErrorCodes
{
    public const string ConfigChanged = "UOM_CONFIG_CHANGED";
    public const string InvalidQuantity = "UOM_INVALID_QUANTITY";
    public const string PrecisionExceeded = "UOM_PRECISION_EXCEEDED";
    public const string NotRepresentable = "UOM_NOT_REPRESENTABLE";
    public const string OptionNotFound = "UOM_OPTION_NOT_FOUND";
    public const string OptionRequired = "UOM_OPTION_REQUIRED";
    public const string InvalidConfiguration = "UOM_INVALID_CONFIGURATION";
    public const string DimensionMismatch = "UOM_DIMENSION_MISMATCH";
    public const string BaseUnitLocked = "UOM_BASE_UNIT_LOCKED";
    public const string UnitInUse = "UOM_UNIT_IN_USE";
    public const string DuplicateUnit = "UOM_DUPLICATE_UNIT";
    public const string InsufficientLooseStock = "UOM_INSUFFICIENT_LOOSE_STOCK";
    public const string InsufficientSealedStock = "UOM_INSUFFICIENT_SEALED_STOCK";
    public const string InsufficientPreparedStock = "UOM_INSUFFICIENT_PREPARED_STOCK";
    public const string InsufficientPackagingStock = "UOM_INSUFFICIENT_PACKAGING_STOCK";
    public const string PackagingCostMissing = "UOM_PACKAGING_COST_MISSING";
    public const string PackStateNotEnabled = "UOM_PACK_STATE_NOT_ENABLED";
    public const string PackStateMismatch = "UOM_PACK_STATE_MISMATCH";
    public const string InvalidPackBreakdown = "UOM_INVALID_PACK_BREAKDOWN";
    public const string QuantityMismatch = "UOM_QUANTITY_MISMATCH";
    public const string RepackNotEnabled = "UOM_REPACK_NOT_ENABLED";
    public const string RepackStatus = "UOM_REPACK_STATUS";
    public const string RepackCostUnreliable = "UOM_REPACK_COST_UNRELIABLE";
    public const string RepackWastageNotSupported = "UOM_REPACK_WASTAGE_NOT_SUPPORTED";
    public const string RepackReverseBlocked = "UOM_REPACK_REVERSE_BLOCKED";
    public const string UnsupportedOperation = "UOM_UNSUPPORTED_OPERATION";
}

/// <summary>A rejected unit input or configuration. Maps to HTTP 400 with <see cref="Code"/>.</summary>
public sealed class UomValidationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>The request was built from state that has since changed. Maps to HTTP 409 with <see cref="Code"/>.</summary>
public sealed class UomConflictException(string code, string message, object? details = null) : Exception(message)
{
    public string Code { get; } = code;
    public object? Details { get; } = details;
}

/// <summary>Exact quantity rules. Nothing here rounds stock: an input that does not fit is rejected (CALC-002).</summary>
public static class UomQuantityMath
{
    /// <summary>Storage limit for a quantity's fractional digits; the database enforces the same scale.</summary>
    public const int StorageScale = 6;

    /// <summary>Removes trailing zeros so 24.00 is stored and displayed as 24.</summary>
    public static decimal Normalize(decimal value) => value / 1.000000000000000000000000000000000m;

    public static int ScaleOf(decimal value) => (decimal.GetBits(Normalize(value))[3] >> 16) & 0xFF;

    public static void EnsurePositive(decimal value, string label)
    {
        if (value <= 0m)
            throw new UomValidationException(UomErrorCodes.InvalidQuantity, $"{label} must be greater than zero.");
    }

    public static void EnsurePrecision(decimal value, int precision, string label)
    {
        if (ScaleOf(value) > precision)
            throw new UomValidationException(
                UomErrorCodes.PrecisionExceeded,
                precision == 0
                    ? $"{label} must be a whole number."
                    : $"{label} allows at most {precision} decimal place(s).");
    }

    /// <summary>Quantity × factor, refusing any result that cannot be stored exactly in the base unit's precision.</summary>
    public static decimal ToBase(decimal entered, decimal numerator, decimal denominator, int basePrecision, string baseUnit)
    {
        if (numerator <= 0m || denominator <= 0m)
            throw new UomValidationException(UomErrorCodes.InvalidConfiguration, "A conversion factor must be positive.");

        var scaled = entered * numerator;
        var result = scaled / denominator;
        if (result * denominator != scaled || ScaleOf(result) > basePrecision)
            throw new UomValidationException(
                UomErrorCodes.NotRepresentable,
                $"{entered} does not convert to an exact quantity of {baseUnit} at {basePrecision} decimal place(s).");
        return Normalize(result);
    }

    /// <summary>A line amount spread over its base quantity; rounding it back gives the amount to the cent.</summary>
    public static decimal PerBaseAmount(decimal amount, decimal baseQuantity)
    {
        var perBase = Normalize(Math.Round(amount / baseQuantity, 12, MidpointRounding.AwayFromZero));
        if (MonetaryRounding.ToCurrency(perBase * baseQuantity) != MonetaryRounding.ToCurrency(amount))
            throw new UomValidationException(
                UomErrorCodes.NotRepresentable,
                $"An amount of {amount:0.00} cannot be represented over a quantity of {baseQuantity}.");
        return perBase;
    }
}

public sealed record ResolvedUnit(long Id, string Code, string Symbol, string Dimension, int Precision, bool IsWhole);

public sealed record ResolvedPurchaseOption(
    long Id,
    string Code,
    string Label,
    ResolvedUnit Unit,
    decimal FactorNumerator,
    decimal FactorDenominator,
    bool IsDefault,
    bool IsSealedPack)
{
    public decimal BaseQuantityPerUnit => FactorNumerator / FactorDenominator;
}

public sealed record ResolvedComponent(long VariantId, decimal Quantity, bool IsRequired);

public sealed record ResolvedSellingOption(
    long Id,
    string Kind,
    string Code,
    string Label,
    ResolvedUnit? Unit,
    decimal? BaseQuantity,
    decimal FactorNumerator,
    decimal FactorDenominator,
    decimal Price,
    bool IsDefault,
    string? SealedPackCode,
    bool IsPreparedEligible,
    IReadOnlyList<ResolvedComponent> Components);

/// <summary>The current configuration of a variant. A variant with no configuration is plain pcs and has none of these.</summary>
public sealed record ResolvedVariantUom(
    long ConfigId,
    int Version,
    long VariantId,
    ResolvedUnit BaseUnit,
    bool PackStateTrackingEnabled,
    bool RepackingEnabled,
    IReadOnlyList<ResolvedPurchaseOption> PurchaseOptions,
    IReadOnlyList<ResolvedSellingOption> SellingOptions);

public sealed record PurchaseLineResolution(decimal BaseQuantity, decimal BaseUnitPrice, UnitTransactionSnapshot Snapshot, ResolvedPurchaseOption Option);

public sealed record SaleLineResolution(decimal BaseQuantity, decimal LineGross, decimal BaseUnitPrice, UnitTransactionSnapshot Snapshot, ResolvedSellingOption Option);

public sealed record StockEntryResolution(decimal BaseQuantity, UnitTransactionSnapshot Snapshot);

/// <summary>Turns an entered quantity into the authoritative base quantity, amount and immutable snapshot. Pure.</summary>
public static class UomLineResolver
{
    public static void EnsureCurrentVersion(ResolvedVariantUom config, long? expectedConfigVersionId)
    {
        if (expectedConfigVersionId.HasValue && expectedConfigVersionId.Value != config.ConfigId)
            throw new UomConflictException(
                UomErrorCodes.ConfigChanged,
                "The unit configuration of this item has changed. Review the updated units and prices.",
                new { VariantId = config.VariantId, CurrentConfigVersionId = config.ConfigId, RequestedConfigVersionId = expectedConfigVersionId.Value });
    }

    public static PurchaseLineResolution ResolvePurchase(
        ResolvedVariantUom config,
        long optionId,
        decimal enteredQuantity,
        decimal enteredUnitPrice,
        long? expectedConfigVersionId)
    {
        EnsureCurrentVersion(config, expectedConfigVersionId);
        var option = config.PurchaseOptions.FirstOrDefault(candidate => candidate.Id == optionId)
            ?? throw new UomValidationException(UomErrorCodes.OptionNotFound, $"Purchase unit {optionId} is not available for this item.");
        if (enteredUnitPrice < 0m)
            throw new UomValidationException(UomErrorCodes.InvalidQuantity, "Unit price cannot be negative.");
        UomQuantityMath.EnsurePositive(enteredQuantity, "Quantity");
        UomQuantityMath.EnsurePrecision(enteredQuantity, option.IsSealedPack ? 0 : option.Unit.Precision, $"Quantity of {option.Label}");

        var baseQuantity = UomQuantityMath.ToBase(
            enteredQuantity, option.FactorNumerator, option.FactorDenominator, config.BaseUnit.Precision, config.BaseUnit.Code);
        var lineAmount = enteredQuantity * enteredUnitPrice;
        return new PurchaseLineResolution(
            baseQuantity,
            UomQuantityMath.PerBaseAmount(lineAmount, baseQuantity),
            new UnitTransactionSnapshot(
                option.Unit.Code, option.Label, UomQuantityMath.Normalize(enteredQuantity), enteredUnitPrice,
                option.FactorNumerator, option.FactorDenominator, config.BaseUnit.Code, config.ConfigId, option.Id, option.Code),
            option);
    }

    public static SaleLineResolution ResolveSale(
        ResolvedVariantUom config,
        long optionId,
        decimal enteredQuantity,
        long? expectedConfigVersionId)
    {
        EnsureCurrentVersion(config, expectedConfigVersionId);
        var option = config.SellingOptions.FirstOrDefault(candidate => candidate.Id == optionId)
            ?? throw new UomValidationException(UomErrorCodes.OptionNotFound, $"Selling option {optionId} is not available for this item.");
        UomQuantityMath.EnsurePositive(enteredQuantity, "Quantity");

        decimal baseQuantity;
        decimal gross;
        string unitCode;
        if (option.Kind == SellingOptionKinds.Pack)
        {
            // For a fixed pack the quantity is a pack count: price and stock both follow the pack, never a per-ml rate.
            UomQuantityMath.EnsurePrecision(enteredQuantity, 0, $"Number of {option.Label}");
            baseQuantity = UomQuantityMath.ToBase(enteredQuantity, option.BaseQuantity!.Value, 1m, config.BaseUnit.Precision, config.BaseUnit.Code);
            gross = enteredQuantity * option.Price;
            unitCode = option.Code;
        }
        else
        {
            var unit = option.Unit!;
            UomQuantityMath.EnsurePrecision(enteredQuantity, unit.Precision, $"Quantity in {unit.Symbol}");
            baseQuantity = UomQuantityMath.ToBase(enteredQuantity, option.FactorNumerator, option.FactorDenominator, config.BaseUnit.Precision, config.BaseUnit.Code);
            gross = MonetaryRounding.ToCurrency(enteredQuantity * option.Price);
            unitCode = unit.Code;
        }

        return new SaleLineResolution(
            baseQuantity,
            gross,
            UomQuantityMath.PerBaseAmount(gross, baseQuantity),
            new UnitTransactionSnapshot(
                unitCode, option.Label, UomQuantityMath.Normalize(enteredQuantity), option.Price,
                option.Kind == SellingOptionKinds.Pack ? option.BaseQuantity!.Value : option.FactorNumerator,
                option.Kind == SellingOptionKinds.Pack ? 1m : option.FactorDenominator,
                config.BaseUnit.Code, config.ConfigId, option.Id, option.Code),
            option);
    }

    /// <summary>A quantity in the base unit or in any purchase unit, for transfers, counts and adjustments (D09).</summary>
    public static StockEntryResolution ResolveStockEntry(
        ResolvedVariantUom config,
        long? purchaseOptionId,
        decimal enteredQuantity,
        long? expectedConfigVersionId)
    {
        EnsureCurrentVersion(config, expectedConfigVersionId);
        if (purchaseOptionId is null)
        {
            UomQuantityMath.EnsurePrecision(enteredQuantity, config.BaseUnit.Precision, $"Quantity in {config.BaseUnit.Symbol}");
            return new StockEntryResolution(
                UomQuantityMath.Normalize(enteredQuantity),
                new UnitTransactionSnapshot(
                    config.BaseUnit.Code, null, UomQuantityMath.Normalize(enteredQuantity), null, 1m, 1m,
                    config.BaseUnit.Code, config.ConfigId, null, null));
        }

        var option = config.PurchaseOptions.FirstOrDefault(candidate => candidate.Id == purchaseOptionId.Value)
            ?? throw new UomValidationException(UomErrorCodes.OptionNotFound, $"Unit option {purchaseOptionId} is not available for this item.");
        UomQuantityMath.EnsurePrecision(enteredQuantity, option.IsSealedPack ? 0 : option.Unit.Precision, $"Quantity of {option.Label}");
        var baseQuantity = UomQuantityMath.ToBase(
            enteredQuantity, option.FactorNumerator, option.FactorDenominator, config.BaseUnit.Precision, config.BaseUnit.Code);
        return new StockEntryResolution(
            baseQuantity,
            new UnitTransactionSnapshot(
                option.Unit.Code, option.Label, UomQuantityMath.Normalize(enteredQuantity), null,
                option.FactorNumerator, option.FactorDenominator, config.BaseUnit.Code, config.ConfigId, option.Id, option.Code));
    }

    /// <summary>A variant without a configuration only ever moves whole pcs, as it always has.</summary>
    public static void EnsureLegacyWholeQuantity(decimal quantity, string label)
    {
        if (UomQuantityMath.ScaleOf(quantity) > 0)
            throw new UomValidationException(UomErrorCodes.PrecisionExceeded, $"{label} must be a whole number for this item.");
    }

    /// <summary>The configured base unit's precision, or whole pcs when the variant has no configuration.</summary>
    public static void EnsureBaseQuantityAllowed(ResolvedVariantUom? config, decimal quantity, string label)
    {
        if (config is null)
            EnsureLegacyWholeQuantity(quantity, label);
        else
            UomQuantityMath.EnsurePrecision(quantity, config.BaseUnit.Precision, label);
    }
}

public sealed record StandardConversionRow(long FromUnitId, long ToUnitId, decimal Numerator, decimal Denominator);

/// <summary>Physical conversions among compatible units only (UOM-003). Packaging sizes are never global (UOM-004).</summary>
public static class UomStandardConverter
{
    /// <returns>1 <paramref name="from"/> = numerator / denominator <paramref name="to"/>.</returns>
    public static (decimal Numerator, decimal Denominator) Resolve(
        ResolvedUnit from,
        ResolvedUnit to,
        IReadOnlyList<StandardConversionRow> conversions)
    {
        if (from.Id == to.Id)
            return (1m, 1m);
        if (!string.Equals(from.Dimension, to.Dimension, StringComparison.Ordinal) || from.Dimension == UomDimensions.Packaging)
            throw new UomValidationException(
                UomErrorCodes.DimensionMismatch,
                $"{from.Symbol} and {to.Symbol} have no standard conversion. Packaging sizes are set per product.");

        var direct = conversions.FirstOrDefault(row => row.FromUnitId == from.Id && row.ToUnitId == to.Id);
        if (direct is not null)
            return (direct.Numerator, direct.Denominator);
        var inverse = conversions.FirstOrDefault(row => row.FromUnitId == to.Id && row.ToUnitId == from.Id);
        if (inverse is not null)
            return (inverse.Denominator, inverse.Numerator);

        throw new UomValidationException(UomErrorCodes.DimensionMismatch, $"No standard conversion exists from {from.Symbol} to {to.Symbol}.");
    }
}

/// <summary>Splits a value across packs so the parts always sum to it; the last pack clears any rounding remainder.</summary>
public static class CapitalizedValueShare
{
    public static decimal For(decimal balanceValue, int balancePacks, int packs)
    {
        if (packs <= 0 || balancePacks <= 0)
            return 0m;
        return packs >= balancePacks
            ? balanceValue
            : Math.Round(balanceValue * packs / balancePacks, 2, MidpointRounding.AwayFromZero);
    }
}
