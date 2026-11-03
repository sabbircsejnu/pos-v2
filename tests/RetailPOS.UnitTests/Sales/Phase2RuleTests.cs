using RetailPOS.Application.Outlets;
using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Application.Sales.Checkout;
using RetailPOS.Application.Settings.Abstractions;

namespace RetailPOS.Tests.Sales;

public sealed class TaxResolverTests
{
    private static readonly SaleTaxPolicy Business = new(true, 10m, false);

    [Fact]
    public void ProductRateOverridesTheBusinessDefault()
    {
        var resolved = TaxResolver.Resolve(Business, new ProductTaxConfiguration(5m, null));

        Assert.Equal(5m, resolved.RatePercent);
        Assert.Equal(TaxClassifications.Standard, resolved.Classification);
    }

    [Fact]
    public void MissingProductRateFallsBackToTheBusinessDefault()
    {
        var resolved = TaxResolver.Resolve(Business, new ProductTaxConfiguration(null, null));

        Assert.Equal(10m, resolved.RatePercent);
    }

    [Fact]
    public void ExplicitZeroIsAZeroRatedProductNotAMissingRate()
    {
        var resolved = TaxResolver.Resolve(Business, new ProductTaxConfiguration(0m, null));

        Assert.Equal(0m, resolved.RatePercent);
        Assert.Equal(TaxClassifications.ZeroRated, resolved.Classification);
    }

    [Theory]
    [InlineData(TaxClassifications.Exempt)]
    [InlineData(TaxClassifications.ZeroRated)]
    public void ClassificationForcesZeroTaxEvenWithARate(string classification)
    {
        var resolved = TaxResolver.Resolve(Business, new ProductTaxConfiguration(15m, classification));

        Assert.Equal(0m, resolved.RatePercent);
        Assert.Equal(classification, resolved.Classification);
    }

    [Fact]
    public void BusinessSwitchOffChargesNoTaxAtAll()
    {
        var resolved = TaxResolver.Resolve(new SaleTaxPolicy(false, 10m, false), new ProductTaxConfiguration(5m, null));

        Assert.Equal(0m, resolved.RatePercent);
        Assert.Equal(TaxClassifications.None, resolved.Classification);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("  ", null)]
    [InlineData("Standard", "standard")]
    [InlineData(" ZERO_RATED ", "zero_rated")]
    public void ConfiguredClassificationIsNormalised(string? input, string? expected) =>
        Assert.Equal(expected, TaxClassifications.NormalizeConfigured(input));

    [Fact]
    public void UnknownClassificationAndOutOfRangeRatesAreRejected()
    {
        Assert.Throws<InvalidOperationException>(() => TaxClassifications.NormalizeConfigured("reduced"));
        Assert.Throws<InvalidOperationException>(() => TaxClassifications.ValidateRate(100.01m));
        Assert.Throws<InvalidOperationException>(() => TaxClassifications.ValidateRate(-0.01m));
        TaxClassifications.ValidateRate(null);
        TaxClassifications.ValidateRate(0m);
        TaxClassifications.ValidateRate(100m);
    }
}

public sealed class BusinessPosSettingsRulesTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    [InlineData(-5)]
    public void CashSessionDurationMustBeBetweenOneAndTwentyHours(int hours) =>
        Assert.Throws<InvalidOperationException>(() =>
            BusinessPosSettingsRules.Validate(new BusinessPosSettingsUpdate(MaxCashSessionDurationHours: hours)));

    [Theory]
    [InlineData(1)]
    [InlineData(20)]
    public void CashSessionDurationAcceptsTheBounds(int hours) =>
        BusinessPosSettingsRules.Validate(new BusinessPosSettingsUpdate(MaxCashSessionDurationHours: hours));

    [Theory]
    [InlineData(-0.01)]
    [InlineData(100.01)]
    public void PercentagesMustBeBetweenZeroAndOneHundred(double value)
    {
        Assert.Throws<InvalidOperationException>(() =>
            BusinessPosSettingsRules.Validate(new BusinessPosSettingsUpdate(MaxManualDiscountPercent: (decimal)value)));
        Assert.Throws<InvalidOperationException>(() =>
            BusinessPosSettingsRules.Validate(new BusinessPosSettingsUpdate(DefaultTaxRate: (decimal)value)));
    }

    [Fact]
    public void TimeZoneMustBeAKnownIanaId()
    {
        BusinessPosSettingsRules.Validate(new BusinessPosSettingsUpdate(DefaultTimeZoneId: "Asia/Dhaka"));
        Assert.Throws<InvalidOperationException>(() =>
            BusinessPosSettingsRules.Validate(new BusinessPosSettingsUpdate(DefaultTimeZoneId: "Mars/Olympus_Mons")));
        Assert.Throws<InvalidOperationException>(() =>
            BusinessPosSettingsRules.Validate(new BusinessPosSettingsUpdate(DefaultTimeZoneId: " ")));
    }

    [Fact]
    public void BlankTaxNameIsRejected() =>
        Assert.Throws<InvalidOperationException>(() =>
            BusinessPosSettingsRules.Validate(new BusinessPosSettingsUpdate(TaxName: " ")));
}

public sealed class InvoiceNumberFormatTests
{
    [Theory]
    [InlineData("DHK01", 2026, 11, 2, 1, "DHK012611021")]
    [InlineData("MAIN", 2027, 1, 9, 120, "MAIN270109120")]
    [InlineData("A", 2026, 12, 31, 99999, "A26123199999")]
    public void RendersPrefixYyMmDdAndUnpaddedSequenceWithoutSeparators(
        string prefix, int year, int month, int day, long sequence, string expected) =>
        Assert.Equal(expected, AllocatedInvoiceNumber.Render(prefix, new DateTime(year, month, day), sequence));
}

public sealed class OutletPosSetupRulesTests
{
    [Theory]
    [InlineData(" dhk01 ", "DHK01")]
    [InlineData("A", "A")]
    [InlineData("ABCDEFGHIJ", "ABCDEFGHIJ")]
    public void PrefixIsTrimmedAndUppercased(string input, string expected) =>
        Assert.Equal(expected, OutletPosSetupRules.NormalizeInvoicePrefix(input));

    [Theory]
    [InlineData("")]
    [InlineData("1ABC")]
    [InlineData("AB-C")]
    [InlineData("ABCDEFGHIJK")]
    [InlineData("AB CD")]
    public void InvalidPrefixesAreRejectedWithAStableCode(string input)
    {
        var exception = Assert.Throws<OutletSetupException>(() => OutletPosSetupRules.NormalizeInvoicePrefix(input));
        Assert.Equal(OutletSetupErrorCodes.InvalidInvoicePrefix, exception.Code);
    }

    [Theory]
    [InlineData("DHK", "DHK", true)]
    [InlineData("DHK", "DHK1", true)]
    [InlineData("DHK1", "DHK", true)]
    [InlineData("DHK", "DHK12", true)]
    [InlineData("DHK", "DHKA", false)]
    [InlineData("DHK1", "DHK2", false)]
    [InlineData("DHK", "CTG", false)]
    public void PrefixesThatExtendEachOtherWithDigitsOnlyCouldProduceTheSameInvoiceNumber(
        string first, string second, bool collides) =>
        Assert.Equal(collides, OutletPosSetupRules.CanProduceCollision(first, second));

    [Fact]
    public void TimeZoneNormalisationDistinguishesUnchangedClearedAndValid()
    {
        Assert.Null(OutletPosSetupRules.NormalizeTimeZone(null));
        Assert.Equal(string.Empty, OutletPosSetupRules.NormalizeTimeZone("  "));
        Assert.Equal("Asia/Dhaka", OutletPosSetupRules.NormalizeTimeZone(" Asia/Dhaka "));
        Assert.Throws<InvalidOperationException>(() => OutletPosSetupRules.NormalizeTimeZone("Nowhere/Land"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TerminalNameIsRequired(string name)
    {
        var exception = Assert.Throws<OutletSetupException>(() => OutletPosSetupRules.NormalizeTerminalName(name));
        Assert.Equal(OutletSetupErrorCodes.InvalidTerminal, exception.Code);
    }

    [Fact]
    public void TerminalNameIsTrimmedAndLengthLimited()
    {
        Assert.Equal("Counter 1", OutletPosSetupRules.NormalizeTerminalName("  Counter 1 "));
        Assert.Throws<OutletSetupException>(() =>
            OutletPosSetupRules.NormalizeTerminalName(new string('x', OutletPosSetupRules.MaxTerminalNameLength + 1)));
    }
}
