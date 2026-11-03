namespace RetailPOS.Application.Sales.Checkout;

/// <summary>The Business-configured cap (percent of the pre-manual amount) on cashier-entered discounts.</summary>
public sealed record SaleDiscountPolicy(decimal MaxManualDiscountPercent);

/// <summary>What was authorised for a manual discount; recorded in the audit trail (P2.2).</summary>
public sealed record ManualDiscountEvidence(
    long CashierId,
    string Scope,
    decimal LineDiscountTotal,
    decimal InvoiceDiscount,
    decimal EffectivePercent,
    decimal LimitPercent,
    bool OverrideUsed);

/// <summary>
/// Server-side authorisation of manual discounts. Fixed amounts are converted to an effective percentage of the
/// amount they reduce (a line's price x quantity, or the invoice subtotal) *before* the manual discount, so automatic
/// pricing-rule discounts, which are already part of the server price, never count against the cashier's limit.
/// A request over the limit is refused outright: it is never silently reduced.
/// </summary>
public static class SaleDiscountAuthorizer
{
    public static ManualDiscountEvidence? Authorize(
        SaleCheckoutTotals totals,
        SaleDiscountPolicy policy,
        bool canApply,
        bool canOverrideLimit,
        long cashierId)
    {
        ArgumentNullException.ThrowIfNull(totals);
        ArgumentNullException.ThrowIfNull(policy);

        var lineDiscountTotal = totals.Lines.Sum(line => line.LineDiscount);
        var invoiceDiscount = totals.InvoiceDiscount;
        if (lineDiscountTotal <= 0m && invoiceDiscount <= 0m)
            return null;

        if (!canApply)
            throw new SaleCheckoutForbiddenException(
                SaleCheckoutErrorCodes.DiscountNotPermitted,
                "You do not have permission to apply manual discounts.");

        string scope;
        decimal effectivePercent;
        if (invoiceDiscount > 0m)
        {
            scope = "invoice";
            effectivePercent = totals.ItemsSubtotal <= 0m ? 100m : invoiceDiscount / totals.ItemsSubtotal * 100m;
        }
        else
        {
            scope = "line";
            effectivePercent = totals.Lines
                .Where(line => line.LineDiscount > 0m)
                .Max(line => line.LineDiscount / (line.Quantity * line.UnitPrice) * 100m);
        }

        var limit = policy.MaxManualDiscountPercent;
        var overLimit = effectivePercent > limit;
        if (overLimit && !canOverrideLimit)
            throw new SaleCheckoutConflictException(
                SaleCheckoutErrorCodes.DiscountLimitExceeded,
                limit <= 0m
                    ? "Manual discounts are not enabled for this business."
                    : $"The discount is {Math.Round(effectivePercent, 2)}% which exceeds the {limit}% limit.",
                new
                {
                    LimitPercent = limit,
                    EffectivePercent = Math.Round(effectivePercent, 4),
                    Scope = scope
                });

        return new ManualDiscountEvidence(
            cashierId,
            scope,
            lineDiscountTotal,
            invoiceDiscount,
            Math.Round(effectivePercent, 4),
            limit,
            overLimit);
    }
}
