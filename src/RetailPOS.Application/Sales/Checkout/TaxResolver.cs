namespace RetailPOS.Application.Sales.Checkout;

public static class TaxClassifications
{
    public const string Standard = "standard";
    public const string ZeroRated = "zero_rated";
    public const string Exempt = "exempt";

    /// <summary>Stored on a sale line when the Business does not charge tax at all.</summary>
    public const string None = "none";

    private static readonly HashSet<string> Configurable = new(StringComparer.Ordinal) { Standard, ZeroRated, Exempt };

    /// <summary>Validates and normalises a product's configured classification; <c>null</c>/blank means unspecified.</summary>
    public static string? NormalizeConfigured(string? classification)
    {
        if (string.IsNullOrWhiteSpace(classification))
            return null;

        var normalized = classification.Trim().ToLowerInvariant();
        if (!Configurable.Contains(normalized))
            throw new InvalidOperationException(
                $"Tax classification '{classification}' is not supported. Use standard, zero_rated or exempt.");
        return normalized;
    }

    public static void ValidateRate(decimal? rate)
    {
        if (rate is < 0m or > 100m)
            throw new InvalidOperationException("Tax rate must be between 0 and 100.");
    }
}

/// <summary>A product's own tax configuration. A null rate is "not configured"; 0 is an explicit 0% rate.</summary>
public sealed record ProductTaxConfiguration(decimal? Rate, string? Classification);

public sealed record ResolvedLineTax(decimal RatePercent, string Classification);

/// <summary>
/// The single tax-selection rule used by cart pricing, checkout, receipts and returns (P2.1): the Business master switch,
/// then product classification, then the product rate, then the Business default rate.
/// </summary>
public static class TaxResolver
{
    public static ResolvedLineTax Resolve(SaleTaxPolicy policy, ProductTaxConfiguration product)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(product);

        if (!policy.Enabled)
            return new ResolvedLineTax(0m, TaxClassifications.None);

        switch (product.Classification)
        {
            case TaxClassifications.Exempt:
                return new ResolvedLineTax(0m, TaxClassifications.Exempt);
            case TaxClassifications.ZeroRated:
                return new ResolvedLineTax(0m, TaxClassifications.ZeroRated);
        }

        if (product.Rate.HasValue)
            return new ResolvedLineTax(
                product.Rate.Value,
                product.Rate.Value == 0m ? TaxClassifications.ZeroRated : TaxClassifications.Standard);

        return new ResolvedLineTax(policy.RatePercent, TaxClassifications.Standard);
    }
}
