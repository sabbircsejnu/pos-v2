using RetailPOS.Core.Money;

namespace RetailPOS.Application.Sales.Checkout;

public sealed record SalePaymentInput(string Method, decimal Amount, decimal? Tendered);

/// <summary>
/// One immutable payment allocation. For cash: <see cref="CashTendered"/> ≥ <see cref="Amount"/>,
/// <see cref="ChangeGiven"/> = tendered − amount and <see cref="NetCashRetained"/> = amount. Non-cash rows carry none of the cash facts.
/// </summary>
public sealed record SalePaymentPlan(
    string Method,
    decimal Amount,
    decimal? CashTendered,
    decimal? ChangeGiven,
    decimal? NetCashRetained);

public static class SalePaymentPlanner
{
    public const string Cash = "cash";

    private static readonly HashSet<string> AllowedMethods = new(StringComparer.Ordinal) { Cash, "card", "mobile" };

    public static string NormalizeMethod(string? method)
    {
        var normalized = method?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(normalized) || !AllowedMethods.Contains(normalized))
            throw new SaleCheckoutValidationException(
                SaleCheckoutErrorCodes.InvalidPayment,
                $"Payment method '{method}' is not supported. Use cash, card or mobile.");
        return normalized;
    }

    /// <param name="singleMethod">Used only when <paramref name="payments"/> is empty; the full payable is allocated to it.</param>
    /// <param name="singleCashTendered">Cash handed over for the single-method path; ignored for card/mobile.</param>
    public static IReadOnlyList<SalePaymentPlan> Plan(
        string singleMethod,
        IReadOnlyList<SalePaymentInput> payments,
        decimal? singleCashTendered,
        decimal payable)
    {
        ArgumentNullException.ThrowIfNull(payments);
        if (payments.Count == 0)
            return [PlanRow(NormalizeMethod(singleMethod), payable, singleCashTendered, payable)];

        var plans = new List<SalePaymentPlan>(payments.Count);
        foreach (var payment in payments)
        {
            var method = NormalizeMethod(payment.Method);
            if (method != Cash && payment.Tendered.HasValue)
                throw new SaleCheckoutValidationException(
                    SaleCheckoutErrorCodes.InvalidPayment,
                    "Tendered amount is only valid for cash payments.");
            plans.Add(PlanRow(method, payment.Amount, payment.Tendered, payable));
        }

        var allocated = plans.Sum(plan => plan.Amount);
        if (allocated != payable)
            throw new SaleCheckoutValidationException(
                SaleCheckoutErrorCodes.PaymentMismatch,
                $"Payment total {allocated:F2} does not match sale total {payable:F2}. Check payment split amounts.");

        return plans;
    }

    private static SalePaymentPlan PlanRow(string method, decimal amount, decimal? tendered, decimal payable)
    {
        if (amount < 0m || MonetaryRounding.ToCurrency(amount) != amount || (amount == 0m && payable > 0m))
            throw new SaleCheckoutValidationException(
                SaleCheckoutErrorCodes.InvalidPayment,
                "Payment amounts must be positive and use at most 2 decimal places.");

        if (method != Cash)
            return new SalePaymentPlan(method, amount, null, null, null);

        if (!tendered.HasValue)
        {
            if (amount == 0m)
                return new SalePaymentPlan(method, amount, 0m, 0m, 0m);
            throw new SaleCheckoutValidationException(
                SaleCheckoutErrorCodes.InvalidPayment,
                "Cash tendered is required for cash payments.");
        }

        if (MonetaryRounding.ToCurrency(tendered.Value) != tendered.Value)
            throw new SaleCheckoutValidationException(
                SaleCheckoutErrorCodes.InvalidPayment,
                "Cash tendered must not have more than 2 decimal places.");
        if (tendered.Value < amount)
            throw new SaleCheckoutValidationException(
                SaleCheckoutErrorCodes.InvalidPayment,
                "Cash tendered is less than the cash amount to be paid.");

        return new SalePaymentPlan(method, amount, tendered.Value, tendered.Value - amount, amount);
    }
}
