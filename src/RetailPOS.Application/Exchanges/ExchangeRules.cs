using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using RetailPOS.Application.Pos;
using RetailPOS.Application.Sales.FinancialPostings;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Money;

namespace RetailPOS.Application.Exchanges;

/// <summary>Stable machine-readable Exchange error codes (LLD §24).</summary>
public static class ExchangeErrorCodes
{
    public const string NotConfigured = "EXCHANGE_NOT_CONFIGURED";
    public const string Disabled = "EXCHANGE_DISABLED";
    public const string InvalidPolicy = "EXCHANGE_INVALID_POLICY";
    public const string PolicyVersionConflict = "EXCHANGE_POLICY_VERSION_CONFLICT";
    public const string SourceNotFound = "EXCHANGE_SOURCE_NOT_FOUND";
    public const string SourceIneligible = "EXCHANGE_SOURCE_INELIGIBLE";
    public const string SourceEvidenceMissing = "EXCHANGE_SOURCE_EVIDENCE_MISSING";
    public const string CustomerMismatch = "EXCHANGE_CUSTOMER_MISMATCH";
    public const string WalkInMultiInvoice = "EXCHANGE_WALKIN_MULTI_INVOICE";
    public const string ItemMismatch = "EXCHANGE_ITEM_MISMATCH";
    public const string ConditionNotSellable = "EXCHANGE_CONDITION_NOT_SELLABLE";
    public const string QuantityExceedsRemaining = "EXCHANGE_QUANTITY_EXCEEDS_REMAINING";
    public const string InvalidRequest = "EXCHANGE_INVALID_REQUEST";
    public const string ApprovalRequired = "EXCHANGE_APPROVAL_REQUIRED";
    public const string ApprovalInvalid = "EXCHANGE_APPROVAL_INVALID";
    public const string ApprovalNotPermitted = "EXCHANGE_APPROVAL_NOT_PERMITTED";
    public const string ApprovalLocked = "EXCHANGE_APPROVAL_LOCKED";
    public const string LowerPriceDisabled = "EXCHANGE_LOWER_PRICE_DISABLED";
    public const string RefundForbidden = "EXCHANGE_REFUND_FORBIDDEN";
    public const string SettlementMismatch = "EXCHANGE_SETTLEMENT_MISMATCH";
    public const string RefundEvidenceRequired = "EXCHANGE_REFUND_EVIDENCE_REQUIRED";
    public const string CashInsufficient = "EXCHANGE_CASH_INSUFFICIENT";
    public const string SessionRequired = "EXCHANGE_SESSION_REQUIRED";
    public const string StockInsufficient = "EXCHANGE_STOCK_INSUFFICIENT";
    public const string StockLocationMissing = "EXCHANGE_STOCK_LOCATION_MISSING";
    public const string PaymentAccountMissing = "EXCHANGE_PAYMENT_ACCOUNT_MISSING";
    public const string AccountingConfigurationMissing = "EXCHANGE_ACCOUNTING_CONFIGURATION_MISSING";
    public const string AccountingPeriodClosed = "EXCHANGE_ACCOUNTING_PERIOD_CLOSED";
    public const string PreviewStale = "EXCHANGE_PREVIEW_STALE";
    public const string IdempotencyMismatch = "EXCHANGE_IDEMPOTENCY_MISMATCH";
    public const string ConcurrencyConflict = "EXCHANGE_CONCURRENCY_CONFLICT";
    public const string PhoneVerificationFailed = "EXCHANGE_PHONE_VERIFICATION_FAILED";
    public const string PhoneVerificationUnavailable = "EXCHANGE_PHONE_VERIFICATION_UNAVAILABLE";
    public const string ReasonRequired = "EXCHANGE_REASON_REQUIRED";
    public const string OutletPrefixMissing = "EXCHANGE_OUTLET_PREFIX_MISSING";
    public const string ReversalBlockedDependency = "EXCHANGE_REVERSAL_BLOCKED_DEPENDENCY";
    public const string AlreadyReversed = "EXCHANGE_ALREADY_REVERSED";
    public const string ReversalStockUnavailable = "EXCHANGE_REVERSAL_STOCK_UNAVAILABLE";
    public const string NotFound = "EXCHANGE_NOT_FOUND";
}

/// <summary>
/// An Exchange rule violation with a stable code and the HTTP status it maps to (400, 403, 404, 409 or 422).
/// The message is safe to show; it never carries internal detail.
/// </summary>
public sealed class ExchangeException(string code, string message, int statusCode, object? details = null) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
    public object? Details { get; } = details;

    public static ExchangeException BadRequest(string code, string message, object? details = null) => new(code, message, 400, details);
    public static ExchangeException Forbidden(string code, string message) => new(code, message, 403);
    public static ExchangeException NotFound(string code, string message) => new(code, message, 404);
    public static ExchangeException Conflict(string code, string message, object? details = null) => new(code, message, 409, details);
    public static ExchangeException Unprocessable(string code, string message, object? details = null) => new(code, message, 422, details);
    public static ExchangeException TooManyRequests(string code, string message) => new(code, message, 429);
}

public static class ExchangeReasonCodes
{
    public const string SizeFitting = "size_fitting";
    public const string ColourDesign = "colour_design";
    public const string WrongProductPurchased = "wrong_product_purchased";
    public const string CustomerPreferenceChanged = "customer_preference_changed";
    public const string Other = "other";

    public static readonly IReadOnlyList<string> All =
        [SizeFitting, ColourDesign, WrongProductPurchased, CustomerPreferenceChanged, Other];
}

public static class ExchangeStatuses
{
    public const string Completed = "completed";
    public const string Reversed = "reversed";
}

public static class ExchangeSettlementDirections
{
    public const string Collect = "collect";
    public const string Refund = "refund";
    public const string None = "none";
}

public static class ExchangeSettlementMethods
{
    public const string Cash = "cash";
    public const string Card = "card";
    public const string Mobile = "mobile";

    public static readonly IReadOnlyList<string> All = [Cash, Card, Mobile];
}

public static class ExchangeApprovalTypes
{
    public const string Window = "window";
    public const string Chain = "chain";
    public const string HighValue = "high_value";
    public const string Refund = "refund";

    public static readonly IReadOnlyList<string> All = [Window, Chain, HighValue, Refund];
}

public static class ExchangePhoneVerificationModes
{
    public const string Optional = "optional";

    /// <summary>
    /// DN-01: no trustworthy phone verification exists, so this mode cannot be saved. A persisted value is never
    /// silently downgraded: registered-customer exchanges fail closed, walk-in invoices keep the OC-06 skip.
    /// </summary>
    public const string Required = "required";
}

public static class ExchangePhoneVerificationResults
{
    public const string NotRequired = "not_required";
    public const string SkippedWalkIn = "skipped_walkin";
    public const string MatchedCustomerRecord = "matched_customer_record";
}

public static class ExchangeNegativeTargetModes
{
    public const string TrackOnly = "track_only";
    public const string DeductFromExchangeSalesperson = "deduct_from_exchange_salesperson";

    public static readonly IReadOnlyList<string> All = [TrackOnly, DeductFromExchangeSalesperson];
}

public static class ExchangeLoyaltyStatuses
{
    /// <summary>The only value written while the Loyalty module is not implemented (D-05).</summary>
    public const string ModuleInactive = "module_inactive";
}

public static class ExchangeSourceKinds
{
    public const string SaleItem = "sale_item";
    public const string ReplacementLine = "exchange_replacement_line";
}

/// <summary>Pure, deterministic Exchange calculations (no I/O). Every money figure is at currency precision.</summary>
public static class ExchangeCalculator
{
    /// <summary>The local calendar date, in the business zone, of an instant (OC-07). Kind-safe under the legacy switch.</summary>
    public static DateOnly LocalDate(DateTime instant, string timeZoneId) =>
        DateOnly.FromDateTime(CashSessionTime.BusinessDateFor(UtcTime.Normalize(instant), timeZoneId));

    /// <summary>OC-07: the anchor date is Day 0 and the whole of anchor + <paramref name="windowDays"/> is still valid.</summary>
    public static DateOnly LastEligibleDate(DateOnly anchor, int windowDays) => anchor.AddDays(windowDays);

    public static bool IsWindowExpired(DateOnly anchor, int windowDays, DateOnly eligibilityDate) =>
        eligibilityDate > LastEligibleDate(anchor, windowDays);

    /// <summary>OC-05: the larger of the two complete legs, never the settlement difference.</summary>
    public static decimal HighValueAmount(decimal totalReturnCredit, decimal totalReplacementValue) =>
        Math.Max(totalReturnCredit, totalReplacementValue);

    public static bool RequiresHighValueApproval(decimal highValueAmount, decimal? threshold) =>
        threshold is > 0m && highValueAmount >= threshold.Value;

    /// <summary>A replacement's chain depth is one more than the deepest source it replaces (sale lines are depth 0).</summary>
    public static bool ChainCountExceeded(int sourceDepth, int? maxExchangeCountPerLineage) =>
        maxExchangeCountPerLineage is { } max && sourceDepth + 1 > max;

    public static string Direction(decimal netAdjustmentGross) =>
        netAdjustmentGross > 0m ? ExchangeSettlementDirections.Collect
        : netAdjustmentGross < 0m ? ExchangeSettlementDirections.Refund
        : ExchangeSettlementDirections.None;

    /// <summary>The cumulative proportional share used by refunds, so refunds and exchanges of one line sum exactly (C-03).</summary>
    public static decimal ReturnShare(decimal recorded, decimal alreadyReturned, decimal quantity, decimal sourceQuantity) =>
        SaleFinancialIntentFactory.ShareOfReturn(recorded, alreadyReturned, quantity, sourceQuantity);

    /// <summary>
    /// OC-06 and DN-01. Walk-in invoices skip verification. With mode <c>required</c> a registered customer fails
    /// closed: no trustworthy verification capability exists (DN-01), and a phone match is never treated as identity.
    /// </summary>
    public static string ClassifyPhoneVerification(string? mode, bool isWalkIn)
    {
        if (!string.Equals(mode, ExchangePhoneVerificationModes.Required, StringComparison.Ordinal))
            return ExchangePhoneVerificationResults.NotRequired;
        if (isWalkIn)
            return ExchangePhoneVerificationResults.SkippedWalkIn;
        throw ExchangeException.Unprocessable(
            ExchangeErrorCodes.PhoneVerificationUnavailable,
            "This business requires phone verification, but trustworthy phone verification is not available yet. " +
            "Ask an administrator to change the exchange policy's phone verification setting.");
    }

    public static ExchangeSalespersonFact SalespersonFact(decimal signedNetAdjustment, string negativeMode)
    {
        var positive = signedNetAdjustment > 0m ? signedNetAdjustment : 0m;
        var negative = signedNetAdjustment < 0m ? signedNetAdjustment : 0m;
        // D-07: a negative adjustment always reduces Net Sales; only this mode lets it reduce the target achievement.
        var targetEffect = string.Equals(negativeMode, ExchangeNegativeTargetModes.DeductFromExchangeSalesperson, StringComparison.Ordinal)
            ? signedNetAdjustment
            : positive;
        return new ExchangeSalespersonFact(positive, negative, signedNetAdjustment, targetEffect);
    }

    public static string Sha256(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    public static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    public static string Quantity(decimal value) => value.ToString("0.######", CultureInfo.InvariantCulture);
}

public sealed record ExchangeSalespersonFact(decimal Positive, decimal Negative, decimal Signed, decimal TargetEffect);

/// <summary>The money of one exchange, frozen at confirmation. Gross = net + VAT for each leg (inclusive and exclusive alike).</summary>
public sealed record ExchangeTotals(
    decimal ReturnNet,
    decimal ReturnVat,
    decimal ReturnCost,
    decimal ReplacementNet,
    decimal ReplacementVat,
    decimal ReplacementDiscount,
    decimal ReplacementCost)
{
    public decimal ReturnGross => ReturnNet + ReturnVat;
    public decimal ReplacementGross => ReplacementNet + ReplacementVat;
    public decimal NetAdjustmentNet => ReplacementNet - ReturnNet;
    public decimal NetAdjustmentVat => ReplacementVat - ReturnVat;
    public decimal NetAdjustmentGross => ReplacementGross - ReturnGross;
    public decimal HighValueAmount => ExchangeCalculator.HighValueAmount(ReturnGross, ReplacementGross);
    public string Direction => ExchangeCalculator.Direction(NetAdjustmentGross);
    public decimal SettlementAmount => Math.Abs(NetAdjustmentGross);
}

public sealed record ExchangeSettlementInput(
    string Direction,
    string Method,
    decimal Amount,
    decimal? Tendered = null,
    string? ExternalReference = null,
    bool ExternalRefundConfirmed = false);

/// <param name="CashEffect">Signed drawer effect: cash kept for a collection (+), cash paid out for a refund (−); 0 otherwise.</param>
public sealed record ExchangeSettlementPlan(
    string Direction,
    string Method,
    decimal Amount,
    decimal? Tendered,
    decimal? ChangeGiven,
    decimal CashEffect,
    string? ExternalReference,
    bool ExternalRefundConfirmed);

public static class ExchangeSettlementPlanner
{
    public const int MaxReferenceLength = 120;

    /// <summary>
    /// EX-07/EX-08/D-09: a collection may be split across cash, card and mobile and must equal the difference exactly;
    /// a refund is one supported method for the exact amount; a card or mobile refund needs a reference and an explicit
    /// confirmation that the external refund succeeded. Nothing moves for a zero difference.
    /// </summary>
    public static IReadOnlyList<ExchangeSettlementPlan> Plan(ExchangeTotals totals, IReadOnlyList<ExchangeSettlementInput> inputs)
    {
        var direction = totals.Direction;
        var amount = totals.SettlementAmount;
        if (direction == ExchangeSettlementDirections.None)
        {
            if (inputs.Count > 0)
                throw ExchangeException.BadRequest(ExchangeErrorCodes.SettlementMismatch, "An even exchange takes no payment and no refund.");
            return [];
        }

        if (inputs.Count == 0)
            throw ExchangeException.BadRequest(
                ExchangeErrorCodes.SettlementMismatch,
                direction == ExchangeSettlementDirections.Collect
                    ? $"Collect {ExchangeCalculator.Money(amount)} from the customer."
                    : $"Refund {ExchangeCalculator.Money(amount)} to the customer.");

        var plans = new List<ExchangeSettlementPlan>(inputs.Count);
        foreach (var input in inputs)
        {
            var method = input.Method?.Trim().ToLowerInvariant() ?? string.Empty;
            if (!ExchangeSettlementMethods.All.Contains(method))
                throw ExchangeException.BadRequest(ExchangeErrorCodes.SettlementMismatch, $"'{input.Method}' is not a supported method.");
            if (!string.Equals(input.Direction?.Trim(), direction, StringComparison.OrdinalIgnoreCase))
                throw ExchangeException.BadRequest(ExchangeErrorCodes.SettlementMismatch, $"This exchange needs a {direction}, not a {input.Direction}.");
            if (input.Amount <= 0m || input.Amount != MonetaryRounding.ToCurrency(input.Amount))
                throw ExchangeException.BadRequest(ExchangeErrorCodes.SettlementMismatch, "Each amount must be positive with at most two decimals.");

            if (direction == ExchangeSettlementDirections.Collect)
            {
                decimal? change = null;
                if (method == ExchangeSettlementMethods.Cash)
                {
                    var tendered = input.Tendered ?? input.Amount;
                    if (tendered < input.Amount || tendered != MonetaryRounding.ToCurrency(tendered))
                        throw ExchangeException.BadRequest(ExchangeErrorCodes.SettlementMismatch, "Cash tendered must cover the cash amount.");
                    change = tendered - input.Amount;
                    plans.Add(new ExchangeSettlementPlan(direction, method, input.Amount, tendered, change, input.Amount, null, false));
                }
                else
                {
                    if (input.Tendered is not null)
                        throw ExchangeException.BadRequest(ExchangeErrorCodes.SettlementMismatch, "Only cash takes a tendered amount.");
                    plans.Add(new ExchangeSettlementPlan(direction, method, input.Amount, null, null, 0m, Reference(input.ExternalReference), false));
                }
            }
            else
            {
                if (input.Tendered is not null)
                    throw ExchangeException.BadRequest(ExchangeErrorCodes.SettlementMismatch, "A refund takes no tendered amount.");
                if (method == ExchangeSettlementMethods.Cash)
                    plans.Add(new ExchangeSettlementPlan(direction, method, input.Amount, null, null, -input.Amount, null, false));
                else
                {
                    var reference = Reference(input.ExternalReference);
                    if (reference is null || !input.ExternalRefundConfirmed)
                        throw ExchangeException.BadRequest(
                            ExchangeErrorCodes.RefundEvidenceRequired,
                            "A card or mobile refund is completed outside the system: enter its reference and confirm it succeeded.");
                    plans.Add(new ExchangeSettlementPlan(direction, method, input.Amount, null, null, 0m, reference, true));
                }
            }
        }

        if (direction == ExchangeSettlementDirections.Refund && plans.Count != 1)
            throw ExchangeException.BadRequest(ExchangeErrorCodes.SettlementMismatch, "A refund is paid with one method.");
        if (plans.Sum(plan => plan.Amount) != amount)
            throw ExchangeException.BadRequest(
                ExchangeErrorCodes.SettlementMismatch,
                $"The {direction} must total exactly {ExchangeCalculator.Money(amount)}.");
        return plans;
    }

    private static string? Reference(string? reference)
    {
        var trimmed = reference?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return null;
        if (trimmed.Length > MaxReferenceLength)
            throw ExchangeException.BadRequest(ExchangeErrorCodes.RefundEvidenceRequired, $"A reference can have at most {MaxReferenceLength} characters.");
        return trimmed;
    }
}

/// <summary>One journal line before account resolution: either a posting slot or a settlement method.</summary>
public sealed record ExchangeJournalLine(string? Slot, string? SettlementMethod, string Description, decimal Debit, decimal Credit);

/// <summary>
/// Exchange journal (LLD §17), mirroring the sale and refund compositions so the books read the same:
/// return leg Dr Sales return / Dr VAT payable / Dr Inventory–Cr COGS (restored cost); replacement leg
/// Dr Sales discount / Cr Sales (net + discount) / Cr VAT payable / Dr COGS–Cr Inventory; settlement Dr the
/// collection accounts or Cr the refund account. It balances exactly by construction and is checked anyway.
/// </summary>
public static class ExchangeJournalComposer
{
    public const string SalesReturn = "sales_return";
    public const string Sales = "sales";
    public const string SalesDiscount = "sales_discount";
    public const string VatPayable = "vat_payable";
    public const string Cogs = "cogs";
    public const string Inventory = "inventory";

    public static IReadOnlyList<ExchangeJournalLine> Compose(ExchangeTotals totals, IReadOnlyList<ExchangeSettlementPlan> settlements, bool reverse = false)
    {
        var lines = new List<ExchangeJournalLine>();
        void Add(string? slot, string? method, string description, decimal debit, decimal credit)
        {
            if (debit == 0m && credit == 0m)
                return;
            lines.Add(reverse
                ? new ExchangeJournalLine(slot, method, description, credit, debit)
                : new ExchangeJournalLine(slot, method, description, debit, credit));
        }

        Add(SalesReturn, null, "Exchange return", totals.ReturnNet, 0m);
        Add(VatPayable, null, "Exchange return VAT", totals.ReturnVat, 0m);
        Add(Inventory, null, "Exchange return to stock", totals.ReturnCost, 0m);
        Add(Cogs, null, "Exchange return COGS reversal", 0m, totals.ReturnCost);

        Add(SalesDiscount, null, "Exchange replacement discount", totals.ReplacementDiscount, 0m);
        Add(Sales, null, "Exchange replacement revenue", 0m, totals.ReplacementNet + totals.ReplacementDiscount);
        Add(VatPayable, null, "Exchange replacement VAT", 0m, totals.ReplacementVat);
        Add(Cogs, null, "Exchange replacement COGS", totals.ReplacementCost, 0m);
        Add(Inventory, null, "Exchange replacement from stock", 0m, totals.ReplacementCost);

        foreach (var settlement in settlements)
            if (settlement.Direction == ExchangeSettlementDirections.Collect)
                Add(null, settlement.Method, $"Exchange collection via {settlement.Method}", settlement.Amount, 0m);
            else
                Add(null, settlement.Method, $"Exchange refund via {settlement.Method}", 0m, settlement.Amount);

        var debit = lines.Sum(line => line.Debit);
        var credit = lines.Sum(line => line.Credit);
        if (debit != credit)
            throw ExchangeException.Unprocessable(
                ExchangeErrorCodes.SettlementMismatch,
                $"The exchange journal would not balance (debit {ExchangeCalculator.Money(debit)}, credit {ExchangeCalculator.Money(credit)}).");
        return lines;
    }
}
