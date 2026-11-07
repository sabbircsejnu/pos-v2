using RetailPOS.Core.Money;

namespace RetailPOS.Application.Sales.Checkout;

public sealed record SaleTaxPolicy(bool Enabled, decimal RatePercent, bool PricesIncludeTax)
{
    public decimal EffectiveRate => Enabled ? RatePercent : 0m;

    public static SaleTaxPolicy None { get; } = new(false, 0m, false);
}

/// <summary>
/// A cart line whose <see cref="UnitPrice"/> has already been resolved by the server pricing pipeline. A null
/// <see cref="TaxRatePercent"/> means "use the policy rate" (a product without its own tax configuration).
/// <see cref="LineGross"/> is set for a unit-configured line: the pack or custom amount is the authority, and
/// <see cref="UnitPrice"/> is that amount spread over the base quantity.
/// </summary>
public sealed record SaleCheckoutLineInput(
    long VariantId,
    decimal Quantity,
    decimal UnitPrice,
    decimal LineDiscount,
    decimal? TaxRatePercent = null,
    string? TaxClassification = null,
    decimal? LineGross = null);

public sealed record SaleCheckoutLineResult(
    long VariantId,
    decimal Quantity,
    decimal UnitPrice,
    decimal LineDiscount,
    decimal Subtotal,
    decimal InvoiceDiscountShare,
    decimal TaxableAmount,
    decimal VatAmount,
    decimal TaxRatePercent = 0m,
    string TaxClassification = TaxClassifications.Standard);

/// <param name="TaxRate">The single tax rate when every taxable line shares one; 0 when rates are mixed (see lines).</param>
public sealed record SaleCheckoutTotals(
    IReadOnlyList<SaleCheckoutLineResult> Lines,
    decimal ItemsSubtotal,
    decimal InvoiceDiscount,
    decimal Tax,
    decimal TotalAmount,
    decimal TaxRate,
    bool TaxInclusive);

/// <summary>
/// Deterministic, side-effect free money calculation for checkout. All amounts are at currency precision and
/// the invoice discount and VAT are distributed to lines with largest-remainder allocation so the line
/// snapshots always add up to the sale totals exactly.
/// </summary>
public static class SaleCheckoutCalculator
{
    public static SaleCheckoutTotals Calculate(
        IReadOnlyList<SaleCheckoutLineInput> lines,
        decimal invoiceDiscount,
        SaleTaxPolicy taxPolicy)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(taxPolicy);
        if (lines.Count == 0)
            throw new SaleCheckoutValidationException(SaleCheckoutErrorCodes.InvalidQuantity, "Sale must have at least one item");

        RequireCurrencyPrecision(invoiceDiscount, "Invoice discount", SaleCheckoutErrorCodes.InvalidDiscount);
        if (invoiceDiscount < 0m)
            throw new SaleCheckoutValidationException(SaleCheckoutErrorCodes.InvalidDiscount, "Invoice discount cannot be negative.");

        var nets = new decimal[lines.Count];
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            if (line.LineGross.HasValue ? line.Quantity <= 0m : line.Quantity < 1)
                throw new SaleCheckoutValidationException(
                    SaleCheckoutErrorCodes.InvalidQuantity,
                    $"Quantity for variant ID {line.VariantId} must be at least 1.");
            if (line.LineGross.HasValue)
                RequireCurrencyPrecision(line.LineGross.Value, "Line amount", SaleCheckoutErrorCodes.InvalidDiscount);
            else
                RequireCurrencyPrecision(line.UnitPrice, "Unit price", SaleCheckoutErrorCodes.InvalidDiscount);
            RequireCurrencyPrecision(line.LineDiscount, "Line discount", SaleCheckoutErrorCodes.InvalidDiscount);
            if (line.UnitPrice < 0m)
                throw new SaleCheckoutValidationException(
                    SaleCheckoutErrorCodes.InvalidDiscount,
                    $"Unit price for variant ID {line.VariantId} cannot be negative.");
            if (line.LineDiscount < 0m)
                throw new SaleCheckoutValidationException(
                    SaleCheckoutErrorCodes.InvalidDiscount,
                    $"Line discount for variant ID {line.VariantId} cannot be negative.");

            var gross = line.LineGross ?? line.Quantity * line.UnitPrice;
            if (line.LineDiscount > gross)
                throw new SaleCheckoutValidationException(
                    SaleCheckoutErrorCodes.InvalidDiscount,
                    $"Line discount for variant ID {line.VariantId} exceeds the line amount.");
            nets[index] = gross - line.LineDiscount;
        }

        if (invoiceDiscount > 0m && lines.Any(line => line.LineDiscount > 0m))
            throw new SaleCheckoutValidationException(
                SaleCheckoutErrorCodes.DiscountConflict,
                "Line discounts and an invoice discount cannot be applied to the same sale.");

        var itemsSubtotal = nets.Sum();
        if (invoiceDiscount > itemsSubtotal)
            throw new SaleCheckoutValidationException(
                SaleCheckoutErrorCodes.InvalidDiscount,
                "Invoice discount exceeds the sale amount.");

        var discountShares = Allocate(invoiceDiscount, nets);
        var bases = new decimal[lines.Count];
        for (var index = 0; index < bases.Length; index++)
            bases[index] = nets[index] - discountShares[index];

        var taxableBase = itemsSubtotal - invoiceDiscount;
        var lineRates = new decimal[lines.Count];
        for (var index = 0; index < lineRates.Length; index++)
        {
            lineRates[index] = lines[index].TaxRatePercent ?? taxPolicy.EffectiveRate;
            if (lineRates[index] is < 0m or > 100m)
                throw new SaleCheckoutValidationException(
                    SaleCheckoutErrorCodes.InvalidDiscount,
                    $"Tax rate for variant ID {lines[index].VariantId} must be between 0 and 100.");
        }

        var inclusive = taxPolicy.PricesIncludeTax && lineRates.Any(rate => rate > 0m);

        // Tax is rounded once per rate group (after discounts) and then allocated to that group's lines, so a
        // single-rate sale gives exactly the same tax as taxing the whole sale at that rate.
        var vatShares = new decimal[lines.Count];
        foreach (var group in Enumerable.Range(0, lines.Count).GroupBy(index => lineRates[index]))
        {
            var rate = group.Key;
            if (rate <= 0m)
                continue;

            var members = group.ToArray();
            var groupBase = members.Sum(index => bases[index]);
            var groupTax = inclusive
                ? MonetaryRounding.ToCurrency(groupBase * rate / (100m + rate))
                : MonetaryRounding.ToCurrency(groupBase * rate / 100m);
            var groupShares = Allocate(groupTax, members.Select(index => bases[index]).ToArray());
            for (var position = 0; position < members.Length; position++)
                vatShares[members[position]] = groupShares[position];
        }

        var tax = vatShares.Sum();
        var total = inclusive ? taxableBase : taxableBase + tax;

        var results = new SaleCheckoutLineResult[lines.Count];
        for (var index = 0; index < results.Length; index++)
        {
            var line = lines[index];
            results[index] = new SaleCheckoutLineResult(
                line.VariantId,
                line.Quantity,
                line.UnitPrice,
                line.LineDiscount,
                nets[index],
                discountShares[index],
                inclusive ? bases[index] - vatShares[index] : bases[index],
                vatShares[index],
                lineRates[index],
                line.TaxClassification
                    ?? (lineRates[index] > 0m ? TaxClassifications.Standard
                        : taxPolicy.Enabled ? TaxClassifications.ZeroRated : TaxClassifications.None));
        }

        var taxableRates = lineRates.Where(rate => rate > 0m).Distinct().ToArray();
        var uniformRate = taxableRates.Length == 1 ? taxableRates[0] : 0m;
        return new SaleCheckoutTotals(results, itemsSubtotal, invoiceDiscount, tax, total, uniformRate, inclusive);
    }

    /// <summary>
    /// Splits <paramref name="total"/> across <paramref name="weights"/> proportionally in whole cents; the
    /// shares always sum to <paramref name="total"/> exactly (largest remainder, ties resolved by position).
    /// </summary>
    public static decimal[] Allocate(decimal total, IReadOnlyList<decimal> weights)
    {
        var shares = new decimal[weights.Count];
        if (total == 0m || weights.Count == 0)
            return shares;

        var weightSum = weights.Sum();
        if (weightSum <= 0m)
            throw new SaleCheckoutValidationException(
                SaleCheckoutErrorCodes.InvalidDiscount,
                "An amount cannot be allocated across lines with no value.");

        var totalCents = (long)MonetaryRounding.ToCurrency(total * 100m);
        var allocatedCents = new long[weights.Count];
        var remainders = new decimal[weights.Count];
        long allocated = 0;
        for (var index = 0; index < weights.Count; index++)
        {
            var exact = totalCents * weights[index] / weightSum;
            var floor = (long)Math.Floor(exact);
            allocatedCents[index] = floor;
            remainders[index] = exact - floor;
            allocated += floor;
        }

        var leftover = (int)(totalCents - allocated);
        foreach (var index in Enumerable.Range(0, weights.Count)
                     .OrderByDescending(position => remainders[position])
                     .ThenBy(position => position)
                     .Take(leftover))
        {
            allocatedCents[index]++;
        }

        for (var index = 0; index < shares.Length; index++)
            shares[index] = allocatedCents[index] / 100m;

        return shares;
    }

    private static void RequireCurrencyPrecision(decimal value, string label, string code)
    {
        if (MonetaryRounding.ToCurrency(value) != value)
            throw new SaleCheckoutValidationException(code, $"{label} must not have more than {MonetaryRounding.CurrencyDecimals} decimal places.");
    }
}
