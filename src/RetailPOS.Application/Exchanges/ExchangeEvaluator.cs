using System.Text;
using RetailPOS.Application.Exchanges.Abstractions;
using RetailPOS.Application.Pos.Abstractions;
using RetailPOS.Application.Pos.ReadModels;
using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Application.Sales.Checkout;
using RetailPOS.Application.Sales.Commands;
using RetailPOS.Application.Settings.Abstractions;
using RetailPOS.Application.Uom;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Exchanges;
using RetailPOS.Core.Entities.Uom;
using RetailPOS.Core.Money;

namespace RetailPOS.Application.Exchanges;

/// <param name="ConditionSellable">EX-05: the cashier confirms the item is in good, sellable condition.</param>
public sealed record ExchangeReturnInput(string SourceKind, long SourceLineId, decimal Quantity, bool ConditionSellable, bool PackagingReusable = false);

/// <summary>The exchange as the cashier composed it. Prices, totals and dates are never taken from here.</summary>
public sealed record ExchangeRequest(
    long ReceivingOutletId,
    long? TerminalId,
    IReadOnlyList<long> SourceSaleIds,
    IReadOnlyList<ExchangeReturnInput> Returns,
    IReadOnlyList<CreateSaleLine> Replacements,
    decimal InvoiceDiscount,
    long? SalespersonUserId,
    string ReasonCode,
    string? ReasonOtherText,
    string? InternalNote);

/// <summary>Who is asking and what they may do, from the authenticated principal only.</summary>
public sealed record ExchangeActor(long UserId, bool CanApplyDiscount, bool CanOverrideDiscountLimit, bool CanRefund);

public sealed record EvaluatedReturn(
    ExchangeReturnInput Input,
    Sale? Sale,
    SaleItem? SaleItem,
    ExchangeReplacementLine? SourceReplacement,
    long VariantId,
    long RootSaleItemId,
    int SourceDepth,
    decimal AlreadyReturned,
    decimal CreditNet,
    decimal CreditVat,
    decimal ContentCost,
    DateOnly AnchorDate,
    bool WindowExpired,
    bool ChainExceeded);

public sealed record EvaluatedReplacement(
    CreateSaleLine Input,
    SaleUomLine Uom,
    PosCartPriceReadModel Priced,
    SaleCheckoutLineResult Line,
    decimal HistoricalUnitCost);

public sealed record EvaluatedExchange(
    long BusinessId,
    ExchangePolicyVersion Policy,
    long ReceivingOutletId,
    long TerminalId,
    string TimeZoneId,
    DateOnly EligibilityLocalDate,
    IReadOnlyList<Sale> SourceSales,
    long CustomerId,
    string PhoneVerificationResult,
    IReadOnlyList<EvaluatedReturn> Returns,
    IReadOnlyList<EvaluatedReplacement> Replacements,
    SaleCheckoutTotals ReplacementTotals,
    ExchangeTotals Totals,
    IReadOnlyList<string> RequiredApprovals,
    string PreviewHash,
    IReadOnlyList<string> Warnings);

/// <summary>
/// The single evaluation shared by preview, supervisor approval and confirmation, so all three see exactly the same
/// eligibility, values and required approvals, and the same preview hash. It performs no writes.
/// </summary>
public sealed class ExchangeEvaluator(
    IExchangeStore store,
    ISaleCreationStore saleStore,
    IPosCartPriceCalculator priceCalculator,
    ISaleCheckoutSettingsProvider checkoutSettings,
    ICompanyTimeZoneProvider timeZones,
    IVariantUomReader? uomReader = null)
{
    public const int MaxLines = 100;
    public const int MaxSourceInvoices = 20;
    private const int MaxNoteLength = 1000;
    private const int MaxReasonLength = 500;

    public Task<decimal> ResolveCostAsync(long variantId, CancellationToken cancellationToken) =>
        saleStore.ResolveHistoricalUnitCostSnapshotAsync(variantId, cancellationToken);

    public async Task<ExchangePolicyVersion> RequireEnabledPolicyAsync(long businessId, CancellationToken cancellationToken)
    {
        var policy = await store.FindCurrentPolicyAsync(businessId, cancellationToken)
            ?? throw ExchangeException.Conflict(ExchangeErrorCodes.NotConfigured, "Product exchange has not been configured for this business yet.");
        if (!policy.IsEnabled)
            throw ExchangeException.Conflict(ExchangeErrorCodes.Disabled, "Product exchange is switched off for this business.");
        return policy;
    }

    /// <param name="sales">Sales loaded (and, on confirmation, locked) by the caller.</param>
    /// <param name="replacementSources">Replacement lines being re-exchanged, loaded (and locked) by the caller.</param>
    /// <param name="nowUtc">Server time: the eligibility date is its local date, never the session date (OC-07).</param>
    public async Task<EvaluatedExchange> EvaluateAsync(
        long businessId,
        ExchangeRequest request,
        ExchangeActor actor,
        IReadOnlyList<Sale> sales,
        IReadOnlyList<ExchangeReplacementLine> replacementSources,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        ValidateShape(request);
        var policy = await RequireEnabledPolicyAsync(businessId, cancellationToken);

        var outletBusiness = await saleStore.GetOutletBusinessIdAsync(request.ReceivingOutletId, cancellationToken);
        if (outletBusiness != businessId)
            throw ExchangeException.NotFound(ExchangeErrorCodes.SourceNotFound, "The receiving outlet was not found.");
        var terminalId = await ExchangeTerminals.ResolveAsync(saleStore, request.ReceivingOutletId, request.TerminalId, cancellationToken);
        if (request.SalespersonUserId is { } salesperson
            && !await saleStore.IsEligibleSalespersonAsync(salesperson, businessId, request.ReceivingOutletId, cancellationToken))
            throw ExchangeException.BadRequest(ExchangeErrorCodes.InvalidRequest, "The selected salesperson is not an active user assigned to this outlet.");

        var zoneId = await timeZones.GetTimeZoneIdAsync(businessId, cancellationToken);
        var eligibilityDate = ExchangeCalculator.LocalDate(nowUtc, zoneId);

        // ---- Sources and customer identity (EX-22, EX-23, D-03) ----
        var saleIds = request.SourceSaleIds.Distinct().ToList();
        if (sales.Count != saleIds.Count || sales.Any(sale => sale.BusinessId != businessId))
            throw ExchangeException.NotFound(ExchangeErrorCodes.SourceNotFound, "An original invoice was not found.");
        foreach (var sale in sales)
            if (!(string.Equals(sale.Status, SaleStatuses.Completed, StringComparison.OrdinalIgnoreCase)
                  || string.Equals(sale.Status, SaleStatuses.PartiallyRefunded, StringComparison.OrdinalIgnoreCase)))
                throw ExchangeException.Unprocessable(ExchangeErrorCodes.SourceIneligible, $"Invoice {sale.SaleNumber} is {sale.Status} and cannot be exchanged.");

        var walkInId = await store.FindWalkInCustomerIdAsync(businessId, cancellationToken);
        bool IsWalkIn(long? customerId) => customerId is null || customerId == walkInId
            || sales.Any(sale => sale.CustomerId == customerId && sale.Customer is { IsSystem: true, CustomerCode: Customer.WalkInCustomerCode });

        var customerIds = sales.Select(sale => sale.CustomerId)
            .Concat(replacementSources.Select(line => (long?)line.Exchange!.CustomerId))
            .Distinct()
            .ToList();
        var sourceCount = sales.Count + replacementSources.Select(line => line.ExchangeId).Distinct().Count();
        if (sourceCount > 1)
        {
            if (customerIds.Any(IsWalkIn))
                throw ExchangeException.Unprocessable(ExchangeErrorCodes.WalkInMultiInvoice,
                    "Walk-in invoices cannot be combined. Exchange each walk-in invoice separately.");
            if (customerIds.Count != 1)
                throw ExchangeException.Unprocessable(ExchangeErrorCodes.CustomerMismatch,
                    "All invoices in one exchange must belong to the same customer.");
        }

        var customerId = customerIds.Single() ?? walkInId
            ?? throw ExchangeException.Unprocessable(ExchangeErrorCodes.SourceIneligible, "The original invoice has no customer record.");
        var phoneResult = ExchangeCalculator.ClassifyPhoneVerification(policy.PhoneVerificationMode, IsWalkIn(customerIds.Single()));

        // ---- Returned lines (EX-03, EX-04, EX-05, EX-09, EX-26, C-03, C-09) ----
        var returns = new List<EvaluatedReturn>(request.Returns.Count);
        var consumed = new Dictionary<(string, long), decimal>();
        foreach (var input in request.Returns)
        {
            if (!input.ConditionSellable)
                throw ExchangeException.Unprocessable(ExchangeErrorCodes.ConditionNotSellable,
                    "Only items in good, sellable condition can be exchanged.");
            if (input.Quantity <= 0m)
                throw ExchangeException.BadRequest(ExchangeErrorCodes.InvalidRequest, "Returned quantities must be greater than zero.");

            var key = (input.SourceKind, input.SourceLineId);
            var earlierInRequest = consumed.GetValueOrDefault(key);
            if (input.SourceKind == ExchangeSourceKinds.SaleItem)
                returns.Add(EvaluateSaleItemReturn(input, sales, policy, zoneId, eligibilityDate, earlierInRequest));
            else if (input.SourceKind == ExchangeSourceKinds.ReplacementLine)
                returns.Add(EvaluateReplacementReturn(input, replacementSources, policy, eligibilityDate, earlierInRequest));
            else
                throw ExchangeException.BadRequest(ExchangeErrorCodes.InvalidRequest, $"'{input.SourceKind}' is not a return source.");
            consumed[key] = earlierInRequest + input.Quantity;
        }

        foreach (var sale in sales)
            if (returns.All(line => line.Sale?.Id != sale.Id))
                throw ExchangeException.BadRequest(ExchangeErrorCodes.InvalidRequest, $"Invoice {sale.SaleNumber} has no returned item.");

        // ---- Replacements at today's price, discounts and VAT (EX-10, EX-27) ----
        var replacements = await PriceReplacementsAsync(businessId, request, actor, cancellationToken);

        var totals = new ExchangeTotals(
            returns.Sum(line => line.CreditNet),
            returns.Sum(line => line.CreditVat),
            returns.Sum(line => line.ContentCost),
            replacements.Lines.Sum(line => line.Line.TaxableAmount),
            replacements.Lines.Sum(line => line.Line.VatAmount),
            replacements.Lines.Sum(line => line.Line.LineDiscount + line.Line.InvoiceDiscountShare),
            replacements.Lines.Sum(line => MonetaryRounding.ToCurrency(line.HistoricalUnitCost * line.Line.Quantity)));

        // ---- Approvals and settlement rules (EX-02, EX-03, EX-04, C-10, OC-02, OC-03, OC-05) ----
        var required = new SortedSet<string>(StringComparer.Ordinal);
        if (returns.Any(line => line.WindowExpired && line.SaleItem is not null))
            required.Add(ExchangeApprovalTypes.Window);
        if (returns.Any(line => line.SourceReplacement is not null && (line.WindowExpired || line.ChainExceeded)))
            required.Add(ExchangeApprovalTypes.Chain);
        if (ExchangeCalculator.RequiresHighValueApproval(totals.HighValueAmount, policy.HighValueApprovalThreshold))
            required.Add(ExchangeApprovalTypes.HighValue);
        if (totals.Direction == ExchangeSettlementDirections.Refund)
        {
            if (!policy.AllowLowerPriceExchange)
                throw ExchangeException.Unprocessable(ExchangeErrorCodes.LowerPriceDisabled,
                    "This business does not allow exchanges for a lower-priced item.");
            if (!actor.CanRefund)
                required.Add(ExchangeApprovalTypes.Refund);
        }

        var warnings = new List<string>();
        var hash = PreviewHash(policy, request, terminalId, eligibilityDate, returns, replacements.Lines, totals, required);
        return new EvaluatedExchange(
            businessId, policy, request.ReceivingOutletId, terminalId, zoneId, eligibilityDate, sales, customerId, phoneResult,
            returns, replacements.Lines, replacements.Totals, totals, required.ToList(), hash, warnings);
    }

    private static void ValidateShape(ExchangeRequest request)
    {
        if (request.SourceSaleIds.Count > MaxSourceInvoices || request.Returns.Count > MaxLines || request.Replacements.Count > MaxLines)
            throw ExchangeException.BadRequest(ExchangeErrorCodes.InvalidRequest, "The exchange has too many invoices or lines.");
        if (request.Returns.Count == 0 || request.Replacements.Count == 0)
            throw ExchangeException.BadRequest(ExchangeErrorCodes.InvalidRequest, "An exchange needs at least one returned item and one replacement.");
        if (!ExchangeReasonCodes.All.Contains(request.ReasonCode))
            throw ExchangeException.BadRequest(ExchangeErrorCodes.ReasonRequired, "Choose an exchange reason.");
        if (request.ReasonCode == ExchangeReasonCodes.Other && string.IsNullOrWhiteSpace(request.ReasonOtherText))
            throw ExchangeException.BadRequest(ExchangeErrorCodes.ReasonRequired, "Describe the reason when 'Other' is selected.");
        if (request.ReasonOtherText?.Trim().Length > MaxReasonLength || request.InternalNote?.Trim().Length > MaxNoteLength)
            throw ExchangeException.BadRequest(ExchangeErrorCodes.InvalidRequest, "The reason or note is too long.");
        if (request.InvoiceDiscount < 0m)
            throw ExchangeException.BadRequest(ExchangeErrorCodes.InvalidRequest, "A discount cannot be negative.");
    }

    private static EvaluatedReturn EvaluateSaleItemReturn(
        ExchangeReturnInput input,
        IReadOnlyList<Sale> sales,
        ExchangePolicyVersion policy,
        string zoneId,
        DateOnly eligibilityDate,
        decimal earlierInRequest)
    {
        var sale = sales.FirstOrDefault(candidate => candidate.Items.Any(item => item.Id == input.SourceLineId))
            ?? throw ExchangeException.Unprocessable(ExchangeErrorCodes.ItemMismatch, "A returned item is not on the selected invoices.");
        var item = sale.Items.Single(candidate => candidate.Id == input.SourceLineId);

        // C-09: only lines with their own allocation and cost evidence; current cost or price is never substituted.
        if (item.TaxableAmount is null || item.VatAmount is null || item.HistoricalUnitCost is null)
            throw ExchangeException.Unprocessable(ExchangeErrorCodes.SourceEvidenceMissing,
                $"An item on invoice {sale.SaleNumber} was sold before its price and cost were recorded in detail, so it cannot be exchanged.");
        EnsureQuantityPrecision(input.Quantity, item.UomUnitCode is null);

        var already = item.ReturnedQuantity + earlierInRequest;
        if (input.Quantity > item.Quantity - already)
            throw ExchangeException.Conflict(ExchangeErrorCodes.QuantityExceedsRemaining,
                $"Only {ExchangeCalculator.Quantity(Math.Max(0m, item.Quantity - already))} of this item can still be returned.");

        var anchor = ExchangeCalculator.LocalDate(sale.SaleDate, zoneId);
        return new EvaluatedReturn(
            input, sale, item, null, item.VariantId, item.Id, 0, already,
            ExchangeCalculator.ReturnShare(item.TaxableAmount.Value, already, input.Quantity, item.Quantity),
            ExchangeCalculator.ReturnShare(item.VatAmount.Value, already, input.Quantity, item.Quantity),
            ExchangeCalculator.ReturnShare(MonetaryRounding.ToCurrency(item.HistoricalUnitCost.Value * item.Quantity), already, input.Quantity, item.Quantity),
            anchor,
            ExchangeCalculator.IsWindowExpired(anchor, policy.ExchangeWindowDays, eligibilityDate),
            false);
    }

    private static EvaluatedReturn EvaluateReplacementReturn(
        ExchangeReturnInput input,
        IReadOnlyList<ExchangeReplacementLine> sources,
        ExchangePolicyVersion policy,
        DateOnly eligibilityDate,
        decimal earlierInRequest)
    {
        if (!policy.AllowReplacementReExchange)
            throw ExchangeException.Unprocessable(ExchangeErrorCodes.SourceIneligible, "Replacement items cannot be exchanged again in this business.");
        var line = sources.FirstOrDefault(candidate => candidate.Id == input.SourceLineId)
            ?? throw ExchangeException.NotFound(ExchangeErrorCodes.SourceNotFound, "The exchanged item was not found.");
        if (line.Exchange is not { Status: ExchangeStatuses.Completed })
            throw ExchangeException.Unprocessable(ExchangeErrorCodes.SourceIneligible, "That exchange was reversed, so its items cannot be exchanged again.");
        EnsureQuantityPrecision(input.Quantity, line.UomUnitCode is null);

        var already = line.ReturnedQuantity + earlierInRequest;
        if (input.Quantity > line.IssuedQuantity - already)
            throw ExchangeException.Conflict(ExchangeErrorCodes.QuantityExceedsRemaining,
                $"Only {ExchangeCalculator.Quantity(Math.Max(0m, line.IssuedQuantity - already))} of this item can still be exchanged.");

        var anchor = DateOnly.FromDateTime(line.Exchange.EligibilityLocalDate);
        return new EvaluatedReturn(
            input, null, null, line, line.VariantId, 0, line.LineageDepth, already,
            ExchangeCalculator.ReturnShare(line.TaxableAmount, already, input.Quantity, line.IssuedQuantity),
            ExchangeCalculator.ReturnShare(line.VatAmount, already, input.Quantity, line.IssuedQuantity),
            ExchangeCalculator.ReturnShare(MonetaryRounding.ToCurrency(line.HistoricalUnitCost * line.IssuedQuantity), already, input.Quantity, line.IssuedQuantity),
            anchor,
            ExchangeCalculator.IsWindowExpired(anchor, policy.ReplacementWindowDays ?? 0, eligibilityDate),
            ExchangeCalculator.ChainCountExceeded(line.LineageDepth, policy.MaxExchangeCountPerLineage));
    }

    private static void EnsureQuantityPrecision(decimal quantity, bool legacyWholeUnits)
    {
        try
        {
            if (legacyWholeUnits)
                UomLineResolver.EnsureLegacyWholeQuantity(quantity, "Returned quantity");
            else
                UomQuantityMath.EnsurePrecision(quantity, UomQuantityMath.StorageScale, "Returned quantity");
        }
        catch (UomValidationException exception)
        {
            throw ExchangeException.BadRequest(ExchangeErrorCodes.InvalidRequest, exception.Message);
        }
    }

    private async Task<(IReadOnlyList<EvaluatedReplacement> Lines, SaleCheckoutTotals Totals)> PriceReplacementsAsync(
        long businessId,
        ExchangeRequest request,
        ExchangeActor actor,
        CancellationToken cancellationToken)
    {
        var variantIds = request.Replacements.Select(line => line.VariantId).Distinct().ToList();
        var unsellable = await saleStore.FindUnsellableVariantIdsAsync(variantIds, businessId, cancellationToken);
        if (unsellable.Count > 0)
            throw ExchangeException.Unprocessable(ExchangeErrorCodes.InvalidRequest, $"Variant ID {string.Join(", ", unsellable)} is not available for sale.");

        try
        {
            var configs = uomReader is null
                ? new Dictionary<long, ResolvedVariantUom>()
                : await uomReader.GetCurrentAsync(businessId, variantIds, cancellationToken);
            var uomLines = request.Replacements.Select(line => SaleUomLines.Resolve(line, configs.GetValueOrDefault(line.VariantId))).ToArray();
            var priced = await priceCalculator.CalculateAsync(
                request.Replacements
                    .Select((line, index) => uomLines[index].Resolution is { } resolution
                        ? new PosCartPriceLine(line.VariantId, request.ReceivingOutletId, uomLines[index].BaseQuantity,
                            Option: new UnitOptionPricing(resolution.Option.Kind, resolution.Option.Price, line.Quantity, resolution.Option.ApplyAutomaticPricingRules))
                        : new PosCartPriceLine(line.VariantId, request.ReceivingOutletId, uomLines[index].BaseQuantity))
                    .ToArray(),
                readFromCache: false,
                cancellationToken);
            for (var index = 0; index < uomLines.Length; index++)
                uomLines[index] = SaleUomLines.WithAppliedRule(uomLines[index], priced[index]);

            var totals = SaleCheckoutCalculator.Calculate(
                request.Replacements
                    .Select((line, index) => new SaleCheckoutLineInput(
                        line.VariantId,
                        uomLines[index].BaseQuantity,
                        uomLines[index].Resolution?.BaseUnitPrice ?? priced[index].FinalPrice,
                        line.DiscountAmount,
                        priced[index].TaxRate,
                        priced[index].TaxClassification,
                        uomLines[index].Resolution?.LineGross))
                    .ToArray(),
                request.InvoiceDiscount,
                await checkoutSettings.GetTaxPolicyAsync(businessId, cancellationToken));
            SaleDiscountAuthorizer.Authorize(
                totals,
                await checkoutSettings.GetDiscountPolicyAsync(businessId, cancellationToken),
                actor.CanApplyDiscount,
                actor.CanOverrideDiscountLimit,
                actor.UserId);

            var lines = new List<EvaluatedReplacement>(uomLines.Length);
            for (var index = 0; index < uomLines.Length; index++)
                lines.Add(new EvaluatedReplacement(
                    request.Replacements[index],
                    uomLines[index],
                    priced[index],
                    totals.Lines[index],
                    await saleStore.ResolveHistoricalUnitCostSnapshotAsync(request.Replacements[index].VariantId, cancellationToken)));
            return (lines, totals);
        }
        catch (UomValidationException exception)
        {
            throw ExchangeException.BadRequest(ExchangeErrorCodes.InvalidRequest, exception.Message);
        }
        catch (SaleCheckoutValidationException exception)
        {
            throw ExchangeException.BadRequest(exception.Code, exception.Message);
        }
        catch (SaleCheckoutForbiddenException exception)
        {
            throw ExchangeException.Forbidden(exception.Code, exception.Message);
        }
    }

    /// <summary>
    /// Covers everything the cashier and any supervisor reviewed: values, dates, policy and required approvals.
    /// Costs are excluded (they never reach the customer and can depend on packaging confirmed at commit).
    /// </summary>
    private static string PreviewHash(
        ExchangePolicyVersion policy,
        ExchangeRequest request,
        long terminalId,
        DateOnly eligibilityDate,
        IReadOnlyList<EvaluatedReturn> returns,
        IReadOnlyList<EvaluatedReplacement> replacements,
        ExchangeTotals totals,
        IEnumerable<string> required)
    {
        var text = new StringBuilder()
            .Append("v1|policy=").Append(policy.Id)
            .Append("|outlet=").Append(request.ReceivingOutletId)
            .Append("|terminal=").Append(terminalId)
            .Append("|date=").Append(eligibilityDate.ToString("yyyy-MM-dd"))
            .Append("|salesperson=").Append(request.SalespersonUserId)
            .Append("|reason=").Append(request.ReasonCode).Append(':').Append(request.ReasonOtherText?.Trim())
            .Append("|returns=");
        foreach (var line in returns.OrderBy(line => line.Input.SourceKind, StringComparer.Ordinal).ThenBy(line => line.Input.SourceLineId).ThenBy(line => line.AlreadyReturned))
            text.Append(line.Input.SourceKind).Append(':').Append(line.Input.SourceLineId).Append(':')
                .Append(ExchangeCalculator.Quantity(line.Input.Quantity)).Append(':')
                .Append(ExchangeCalculator.Money(line.CreditNet)).Append(':').Append(ExchangeCalculator.Money(line.CreditVat))
                .Append(':').Append(line.Input.PackagingReusable ? 1 : 0).Append(';');
        text.Append("|replacements=");
        foreach (var line in replacements)
            text.Append(line.Input.VariantId).Append(':').Append(line.Input.SellingOptionId).Append(':')
                .Append(ExchangeCalculator.Quantity(line.Line.Quantity)).Append(':').Append(line.Input.FulfilmentState).Append(':')
                .Append(line.Input.IncludePackaging ? 1 : 0).Append(':')
                .Append(ExchangeCalculator.Money(line.Line.TaxableAmount)).Append(':').Append(ExchangeCalculator.Money(line.Line.VatAmount))
                .Append(':').Append(ExchangeCalculator.Money(line.Line.LineDiscount + line.Line.InvoiceDiscountShare)).Append(';');
        text.Append("|net=").Append(ExchangeCalculator.Money(totals.NetAdjustmentGross))
            .Append("|high=").Append(ExchangeCalculator.Money(totals.HighValueAmount))
            .Append("|approvals=").Append(string.Join(',', required));
        return ExchangeCalculator.Sha256(text.ToString());
    }
}

/// <summary>
/// L6: the terminal an exchange or reversal is recorded on — active, of the receiving outlet (and so of the business).
/// Resolved at Preview and again at Confirm; Confirm also locks the row (FOR SHARE) so it cannot be deactivated mid-commit.
/// </summary>
public static class ExchangeTerminals
{
    public static async Task<long> ResolveAsync(
        RetailPOS.Application.Sales.Abstractions.ISaleCreationStore saleStore, long outletId, long? requestedTerminalId, CancellationToken cancellationToken)
    {
        long? terminalId;
        try
        {
            terminalId = await saleStore.ResolveActiveTerminalIdAsync(outletId, requestedTerminalId, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            throw Unavailable(requestedTerminalId);
        }

        return terminalId ?? throw ExchangeException.Unprocessable(ExchangeErrorCodes.SessionRequired, "No active terminal is configured for this outlet.");
    }

    public static ExchangeException Unavailable(long? requestedTerminalId) => requestedTerminalId is null
        ? ExchangeException.Unprocessable(ExchangeErrorCodes.SessionRequired, "This outlet has more than one active terminal. Select the terminal for this exchange.")
        : ExchangeException.Unprocessable(ExchangeErrorCodes.SessionRequired, "The selected terminal is not active at this outlet. Select an active terminal and review the exchange again.");
}
