using RetailPOS.Application.Sales.Checkout;
using RetailPOS.Application.Sales.Commands;
using RetailPOS.Application.Uom;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Uom;
using Xunit;

namespace RetailPOS.UnitTests.Uom;

public sealed class UomCoreTests
{
    private static readonly ResolvedUnit Ml = new(1, "ml", "ml", UomDimensions.Volume, 2, false);
    private static readonly ResolvedUnit Litre = new(2, "L", "L", UomDimensions.Volume, 4, false);
    private static readonly ResolvedUnit Kg = new(3, "kg", "kg", UomDimensions.Mass, 4, false);
    private static readonly ResolvedUnit Gram = new(4, "g", "g", UomDimensions.Mass, 2, false);
    private static readonly ResolvedUnit Jar = new(5, "jar", "jar", UomDimensions.Packaging, 0, true);
    private static readonly ResolvedUnit Pcs = new(6, "pcs", "pcs", UomDimensions.Count, 0, true);
    private static readonly IReadOnlyList<StandardConversionRow> Standard =
    [
        new(Litre.Id, Ml.Id, 1000m, 1m),
        new(Kg.Id, Gram.Id, 1000m, 1m)
    ];

    private static ResolvedVariantUom Attar(bool tracking = false) => new(
        77,
        1,
        9,
        Ml,
        tracking,
        false,
        [
            new ResolvedPurchaseOption(10, "JAR500", "500 ml Jar", Jar, 500m, 1m, true, tracking),
            new ResolvedPurchaseOption(11, "L1", "Litre", Litre, 1000m, 1m, false, false),
            new ResolvedPurchaseOption(12, "J333", "333 ml Jar", Jar, 333m, 1m, false, false)
        ],
        [
            new ResolvedSellingOption(20, SellingOptionKinds.Pack, "P12", "12 ml", null, 12m, 1m, 1m, 100m, false, null, tracking, [new ResolvedComponent(500, 1m, true)]),
            new ResolvedSellingOption(21, SellingOptionKinds.Custom, "ML", "Custom ml", Ml, null, 1m, 1m, 5m, false, null, false, [new ResolvedComponent(500, 1m, false)]),
            new ResolvedSellingOption(22, SellingOptionKinds.Custom, "L", "Custom L", Litre, null, 1000m, 1m, 4000m, false, null, false, []),
            new ResolvedSellingOption(23, SellingOptionKinds.Pack, "J500", "Sealed jar", null, 500m, 1m, 1m, 1500m, false, tracking ? "JAR500" : null, false, [])
        ]);

    private static CreateSaleLine Line(decimal quantity, long? option = 20, string? state = null, bool packaging = false, long? version = 77) =>
        new(9, quantity, 100m, 0m, null, null, option, version, state, packaging);

    [Theory]
    [InlineData(2, 500, 1, 1000)]
    [InlineData(1.5, 1000, 1, 1500)]
    [InlineData(250, 1, 1000, 0.25)]
    public void Conversion_IsExact(decimal entered, decimal numerator, decimal denominator, decimal expected) =>
        Assert.Equal(expected, UomQuantityMath.ToBase(entered, numerator, denominator, 2, "ml"));

    [Fact]
    public void Conversion_RefusesResultsTheBaseUnitCannotHold()
    {
        var error = Assert.Throws<UomValidationException>(() => UomQuantityMath.ToBase(1m, 1m, 3m, 2, "ml"));
        Assert.Equal(UomErrorCodes.NotRepresentable, error.Code);
        Assert.Throws<UomValidationException>(() => UomQuantityMath.ToBase(0.001m, 1m, 1m, 2, "ml"));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(-1, 1)]
    public void Conversion_RefusesNonPositiveFactors(decimal numerator, decimal denominator) =>
        Assert.Equal(UomErrorCodes.InvalidConfiguration, Assert.Throws<UomValidationException>(() => UomQuantityMath.ToBase(1m, numerator, denominator, 2, "ml")).Code);

    [Fact]
    public void Precision_RejectsInsteadOfRounding()
    {
        var error = Assert.Throws<UomValidationException>(() => UomQuantityMath.EnsurePrecision(12.5678m, 2, "Quantity"));
        Assert.Equal(UomErrorCodes.PrecisionExceeded, error.Code);
        UomQuantityMath.EnsurePrecision(12.50m, 1, "Quantity");
    }

    [Fact]
    public void Normalize_RemovesTrailingZeros() =>
        Assert.Equal("24", UomQuantityMath.Normalize(24.000000m).ToString(System.Globalization.CultureInfo.InvariantCulture));

    [Fact]
    public void Standard_ResolvesBothDirections_AndRefusesOtherDimensions()
    {
        Assert.Equal((1000m, 1m), UomStandardConverter.Resolve(Litre, Ml, Standard));
        Assert.Equal((1m, 1000m), UomStandardConverter.Resolve(Ml, Litre, Standard));
        Assert.Equal((1m, 1m), UomStandardConverter.Resolve(Ml, Ml, Standard));
        Assert.Equal(UomErrorCodes.DimensionMismatch, Assert.Throws<UomValidationException>(() => UomStandardConverter.Resolve(Kg, Ml, Standard)).Code);
        // A jar has no global size.
        Assert.Equal(UomErrorCodes.DimensionMismatch, Assert.Throws<UomValidationException>(() => UomStandardConverter.Resolve(Jar, Jar with { Id = 99 }, Standard)).Code);
    }

    [Fact]
    public void Purchase_OneJarAtAThousand_IsFiveHundredMlAtTwo()
    {
        var line = UomLineResolver.ResolvePurchase(Attar(), 10, 1m, 1000m, 77);
        Assert.Equal(500m, line.BaseQuantity);
        Assert.Equal(2m, line.BaseUnitPrice);
        Assert.Equal(1000m, line.Snapshot.EnteredUnitPrice);
        Assert.Equal("JAR500", line.Snapshot.OptionCode);
        Assert.Equal(77, line.Snapshot.ConfigVersionId);
    }

    [Fact]
    public void Purchase_UnevenPrice_StillRoundsBackToTheExactLine()
    {
        var line = UomLineResolver.ResolvePurchase(Attar(), 12, 1m, 1000m, null);
        Assert.Equal(1000m, Math.Round(line.BaseQuantity * line.BaseUnitPrice, 2));
    }

    [Fact]
    public void Sale_PackAndCustom_HaveIndependentPrices()
    {
        var pack = UomLineResolver.ResolveSale(Attar(), 20, 2m, 77);
        Assert.Equal((24m, 200m), (pack.BaseQuantity, pack.LineGross));
        var custom = UomLineResolver.ResolveSale(Attar(), 21, 24m, 77);
        Assert.Equal((24m, 120m), (custom.BaseQuantity, custom.LineGross));
        var litre = UomLineResolver.ResolveSale(Attar(), 22, 0.25m, 77);
        Assert.Equal((250m, 1000m), (litre.BaseQuantity, litre.LineGross));
    }

    [Fact]
    public void Sale_RefusesFractionOfAPack_UnknownOption_AndStaleVersion()
    {
        Assert.Equal(UomErrorCodes.PrecisionExceeded, Assert.Throws<UomValidationException>(() => UomLineResolver.ResolveSale(Attar(), 20, 1.5m, null)).Code);
        Assert.Equal(UomErrorCodes.OptionNotFound, Assert.Throws<UomValidationException>(() => UomLineResolver.ResolveSale(Attar(), 999, 1m, null)).Code);
        Assert.Equal(UomErrorCodes.ConfigChanged, Assert.Throws<UomConflictException>(() => UomLineResolver.ResolveSale(Attar(), 20, 1m, 76)).Code);
    }

    [Fact]
    public void StockEntry_ConvertsPurchaseUnit_OrTakesBaseAtItsPrecision()
    {
        Assert.Equal(1000m, UomLineResolver.ResolveStockEntry(Attar(), 10, 2m, null).BaseQuantity);
        Assert.Equal(175.5m, UomLineResolver.ResolveStockEntry(Attar(), null, 175.5m, null).BaseQuantity);
        Assert.Throws<UomValidationException>(() => UomLineResolver.ResolveStockEntry(Attar(), null, 1.123m, null));
    }

    [Fact]
    public void SaleForm_DefaultsAndConflicts()
    {
        var tracked = Attar(true);
        Assert.Equal(PackStates.Loose, SaleUomLines.Resolve(Line(1m), tracked).Form!.State);
        Assert.Equal(PackStates.Prepared, SaleUomLines.Resolve(Line(2m, state: "prepared"), tracked).Form!.State);
        Assert.Equal(PackStates.Sealed, SaleUomLines.Resolve(Line(1m, option: 23), tracked).Form!.State);
        Assert.Equal(UomErrorCodes.PackStateMismatch, Assert.Throws<UomValidationException>(() => SaleUomLines.Resolve(Line(1m, option: 23, state: "loose"), tracked)).Code);
        Assert.Equal(UomErrorCodes.PackStateMismatch, Assert.Throws<UomValidationException>(() => SaleUomLines.Resolve(Line(1m, state: "sealed"), tracked)).Code);
        Assert.Null(SaleUomLines.Resolve(Line(1m), Attar()).Form);
        Assert.Equal(UomErrorCodes.PackStateNotEnabled, Assert.Throws<UomValidationException>(() => SaleUomLines.Resolve(Line(1m, state: "loose"), Attar())).Code);
    }

    [Fact]
    public void Packaging_OnlyForFilledStock_AndOncePerCustomLine()
    {
        var tracked = Attar(true);
        Assert.Equal(2m, Assert.Single(SaleUomLines.Resolve(Line(2m), tracked).Packaging).Quantity);
        // A prepared pack already is its packaging.
        Assert.Empty(SaleUomLines.Resolve(Line(2m, state: "prepared"), tracked).Packaging);
        Assert.Empty(SaleUomLines.Resolve(Line(1m, option: 23), tracked).Packaging);
        Assert.Empty(SaleUomLines.Resolve(Line(75m, option: 21), Attar()).Packaging);
        Assert.Equal(1m, Assert.Single(SaleUomLines.Resolve(Line(75m, option: 21, packaging: true), Attar()).Packaging).Quantity);
    }

    [Fact]
    public void LegacyLine_IsWholeOnly_AndConfiguredItemsNeedAnOption()
    {
        Assert.Null(SaleUomLines.Resolve(Line(2m, option: null, version: null), null).Resolution);
        Assert.Equal(UomErrorCodes.PrecisionExceeded, Assert.Throws<UomValidationException>(() => SaleUomLines.Resolve(Line(1.5m, option: null, version: null), null)).Code);
        Assert.Equal(UomErrorCodes.OptionRequired, Assert.Throws<UomValidationException>(() => SaleUomLines.Resolve(Line(1m, option: null, version: null), Attar())).Code);
    }

    [Fact]
    public void Breakdown_SplitsAndValidates()
    {
        var snapshot = new UnitTransactionSnapshot("jar", "Jar", 3m, 1000m, 500m, 1m, "ml", 77, 10, "JAR500");
        Assert.Equal(new ReceiptPackBreakdown(2, 500m, "JAR500", 300m), PurchaseUomRules.Breakdown(1300m, 2, 300m, snapshot, true));
        Assert.Equal(new ReceiptPackBreakdown(2, 500m, "JAR500", 100m), PurchaseUomRules.Breakdown(1100m, null, null, snapshot, true));
        Assert.Equal(UomErrorCodes.QuantityMismatch, Assert.Throws<UomValidationException>(() => PurchaseUomRules.Breakdown(1300m, 2, 200m, snapshot, true)).Code);
        Assert.Equal(UomErrorCodes.InvalidPackBreakdown, Assert.Throws<UomValidationException>(() => PurchaseUomRules.Breakdown(500m, 1, 0m, snapshot, false)).Code);
    }

    [Fact]
    public void PerBaseCost_UsesTheOrdersOwnConversion()
    {
        var snapshot = new UnitTransactionSnapshot("jar", "Jar", 1m, 1000m, 500m, 1m, "ml", 77, 10, "JAR500");
        Assert.Equal(2.4m, PurchaseUomRules.PerBaseCost(1200m, snapshot));
        Assert.Equal(1200m, PurchaseUomRules.PerBaseCost(1200m, null));
    }

    [Fact]
    public void CapitalizedShare_ClearsOnTheLastPack()
    {
        Assert.Equal(3.33m, CapitalizedValueShare.For(10m, 3, 1));
        Assert.Equal(10m, CapitalizedValueShare.For(10m, 3, 3));
        Assert.Equal(16m, CapitalizedValueShare.For(160m, 20, 2));
        Assert.Equal(0m, CapitalizedValueShare.For(10m, 0, 1));
    }

    private static VariantUomConfigInput Config(
        IReadOnlyList<PurchaseOptionInput>? purchase = null,
        IReadOnlyList<SellingOptionInput>? selling = null,
        long baseUnit = 1,
        bool tracking = false,
        bool repacking = false) =>
        new(baseUnit, tracking, repacking, purchase ?? [], selling ?? [new SellingOptionInput(SellingOptionKinds.Custom, "ML", "ml", 1, null, 5m, false, null, false, [])]);

    private static readonly IReadOnlyDictionary<long, ResolvedUnit> Units =
        new[] { Ml, Litre, Kg, Gram, Jar, Pcs }.ToDictionary(unit => unit.Id);

    private static string InvalidCode(VariantUomConfigInput input) =>
        Assert.Throws<UomValidationException>(() => VariantUomConfigValidator.Validate(input, Units, Standard)).Code;

    [Fact]
    public void Config_UsesTheStandardFactor_ForPhysicalUnits_AndTheEnteredOneForPackaging()
    {
        var plan = VariantUomConfigValidator.Validate(
            Config([
                new PurchaseOptionInput("L1", Litre.Id, "Litre", 7m, 7m, true, false),
                new PurchaseOptionInput("J", Jar.Id, "Jar", 500m, 1m, false, false)
            ]),
            Units,
            Standard);
        Assert.Equal(1000m, plan.Purchase[0].FactorNumerator);
        Assert.Equal(500m, plan.Purchase[1].FactorNumerator);
    }

    [Fact]
    public void Config_RejectsWhatTheSrsForbids()
    {
        Assert.Equal(UomErrorCodes.InvalidConfiguration, InvalidCode(Config(baseUnit: Jar.Id)));
        Assert.Equal(UomErrorCodes.InvalidConfiguration, InvalidCode(Config([
            new PurchaseOptionInput("A", Jar.Id, "A", 1m, 1m, false, false)])));
        Assert.Equal(UomErrorCodes.InvalidConfiguration, InvalidCode(Config([
            new PurchaseOptionInput("A", Jar.Id, "A", 1m, 1m, true, false),
            new PurchaseOptionInput("a", Jar.Id, "B", 2m, 1m, false, false)])));
        Assert.Equal(UomErrorCodes.InvalidConfiguration, InvalidCode(Config([
            new PurchaseOptionInput("A", Jar.Id, "A", 0m, 1m, true, false)])));
        Assert.Equal(UomErrorCodes.NotRepresentable, InvalidCode(Config([
            new PurchaseOptionInput("A", Jar.Id, "A", 1m, 3m, true, false)])));
        Assert.Equal(UomErrorCodes.DimensionMismatch, InvalidCode(Config([
            new PurchaseOptionInput("KG", Kg.Id, "kg", 1m, 1m, true, false)])));
        Assert.Equal(UomErrorCodes.InvalidConfiguration, InvalidCode(Config(selling: [])));
        Assert.Equal(UomErrorCodes.InvalidConfiguration, InvalidCode(Config(selling: [
            new SellingOptionInput(SellingOptionKinds.Pack, "P", "P", null, 12m, -1m, false, null, false, [])])));
        Assert.Equal(UomErrorCodes.InvalidConfiguration, InvalidCode(Config(selling: [
            new SellingOptionInput(SellingOptionKinds.Pack, "P", "P", null, 12m, 1.234m, false, null, false, [])])));
        Assert.Equal(UomErrorCodes.PrecisionExceeded, InvalidCode(Config(selling: [
            new SellingOptionInput(SellingOptionKinds.Pack, "P", "P", null, 1.123m, 1m, false, null, false, [])])));
        Assert.Equal(UomErrorCodes.InvalidConfiguration, InvalidCode(Config(selling: [
            new SellingOptionInput(SellingOptionKinds.Pack, "P", "P", null, 12m, 1m, false, null, true, [])])));
        Assert.Equal(UomErrorCodes.InvalidConfiguration, InvalidCode(Config(repacking: true, tracking: true)));
    }

    [Fact]
    public void Snapshot_CannotBeRewritten()
    {
        var line = new SaleItem();
        var snapshot = new UnitTransactionSnapshot("ml", "12 ml", 2m, 100m, 12m, 1m, "ml", 77, 20, "P12");
        line.CaptureUomSnapshot(snapshot);
        Assert.Equal(snapshot, line.ReadUomSnapshot());
        Assert.Throws<InvalidOperationException>(() => line.CaptureUomSnapshot(snapshot));
        Assert.Null(new SaleItem().ReadUomSnapshot());
    }
}
