using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Pos.Abstractions;
using RetailPOS.Application.Pos.ReadModels;
using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Application.Sales.Checkout;
using RetailPOS.Application.Sales.FinancialPostings;
using RetailPOS.Application.Sales.ReadModels;
using RetailPOS.Application.Uom;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Uom;

namespace RetailPOS.Application.Sales.Commands;

/// <summary>
/// A checkout line as submitted by the client. <see cref="UnitPrice"/> is only an assertion of the price the
/// cashier saw: the server re-prices every line and rejects the sale when they differ.
/// <see cref="DiscountAmount"/> is a manual line discount; pricing-rule discounts are already part of the server price.
/// <see cref="AppliedRuleId"/>/<see cref="AppliedRuleName"/> are ignored; the server records the rule it applied.
/// With a <see cref="SellingOptionId"/>, <see cref="Quantity"/> is a pack count or a measured quantity of the option's
/// unit and <see cref="UnitPrice"/> asserts the option's price. Without one, it is whole pcs as it has always been.
/// </summary>
public sealed record CreateSaleLine(
    long VariantId,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountAmount,
    long? AppliedRuleId,
    string? AppliedRuleName,
    long? SellingOptionId = null,
    long? ExpectedConfigVersionId = null,
    string? FulfilmentState = null,
    bool IncludePackaging = false);

public sealed record CreateSalePayment(
    string Method,
    decimal Amount,
    decimal? Tendered);

/// <summary>
/// <paramref name="CashierId"/> must come from the authenticated principal. <paramref name="Tax"/> is an
/// assertion checked against the server calculation; <paramref name="Discount"/> is the manual invoice discount.
/// </summary>
public sealed record CreateSaleCommand(
    long OutletId,
    long? TerminalId,
    long? CustomerId,
    IReadOnlyList<CreateSaleLine> Items,
    decimal Discount,
    decimal Tax,
    string PaymentMethod,
    IReadOnlyList<CreateSalePayment> Payments,
    long CashierId,
    long? OriginalSaleId,
    DateTime? SalesDate,
    string? IdempotencyKey,
    decimal? CashTendered = null,
    bool CanApplyDiscount = false,
    bool CanOverrideDiscountLimit = false,
    long? SalespersonUserId = null) : ICommand<SaleReadModel>;

public sealed class CreateSaleCommandHandler(
    ISaleCreationStore store,
    ISaleCashSessionValidator cashSessionValidator,
    ISaleInvoiceNumberAllocator invoiceNumberAllocator,
    ISaleFinancialPostingWriter financialPostings,
    ISaleCreationAuditWriter auditWriter,
    ISaleStockLedgerWriter stockLedgerWriter,
    ISaleStockCacheInvalidator stockCacheInvalidator,
    ISaleCompletedPublisher saleCompletedPublisher,
    ISaleCreationLogger logger,
    IPosCartPriceCalculator priceCalculator,
    ISaleCheckoutSettingsProvider checkoutSettings,
    IVariantUomReader? uomReader = null,
    IPackStateLedger? packState = null,
    RetailPOS.Application.Inventory.Abstractions.IStockLedgerWriter? rawStockLedger = null)
    : IRequestHandler<CreateSaleCommand, SaleReadModel>
{
    private const decimal PriceTolerance = 0.005m;
    private const decimal TaxTolerance = 0.01m;

    public async Task<SaleReadModel> Handle(CreateSaleCommand request, CancellationToken cancellationToken)
    {
        if (request.Items.Count == 0)
            throw new InvalidOperationException("Sale must have at least one item");

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            throw new SaleCheckoutValidationException(
                SaleCheckoutErrorCodes.IdempotencyKeyRequired,
                "An idempotency key is required to complete a sale.");

        var fingerprint = SaleRequestFingerprint.Compute(request);
        var replay = await TryReplayAsync(request, fingerprint, cancellationToken);
        if (replay is not null)
            return replay;

        var businessId = await store.GetOutletBusinessIdAsync(request.OutletId, cancellationToken)
            ?? throw new KeyNotFoundException($"Outlet with ID {request.OutletId} not found");

        long? customerId = request.CustomerId;
        Customer? customer = null;
        if (customerId.HasValue)
        {
            // A customer of another Business is reported as missing so its existence is not disclosed.
            customer = await store.FindCustomerAsync(customerId.Value, businessId, cancellationToken);
            if (customer is null)
                throw new KeyNotFoundException($"Customer with ID {customerId.Value} not found");
            if (!customer.IsActive)
                throw new InvalidOperationException($"Customer with ID {customerId.Value} is inactive");
        }
        else
        {
            customer = await EnsureWalkInCustomerAsync(businessId, cancellationToken);
            customerId = customer.Id;
        }

        // The salesperson is credited, not authenticated: it must be an active user of this Business assigned to the outlet.
        if (request.SalespersonUserId.HasValue &&
            !await store.IsEligibleSalespersonAsync(request.SalespersonUserId.Value, businessId, request.OutletId, cancellationToken))
            throw new SaleCheckoutValidationException(
                SaleCheckoutErrorCodes.SalespersonNotEligible,
                "The selected salesperson is not an active user assigned to this outlet.");

        await using var transaction = await store.BeginTransactionAsync(cancellationToken);
        try
        {
            var variantIds = request.Items.Select(item => item.VariantId).Distinct().ToArray();
            var unsellable = await store.FindUnsellableVariantIdsAsync(variantIds, businessId, cancellationToken);
            if (unsellable.Count > 0)
                throw new SaleCheckoutValidationException(
                    SaleCheckoutErrorCodes.VariantNotSellable,
                    $"Variant ID {string.Join(", ", unsellable)} is not available for sale.");

            // A unit-configured line is priced from its option at the version in force now; the client's copy is only compared.
            var uomConfigs = uomReader is null
                ? new Dictionary<long, ResolvedVariantUom>()
                : await uomReader.GetCurrentAsync(businessId, variantIds, cancellationToken);
            var uomLines = request.Items
                .Select(item => SaleUomLines.Resolve(item, uomConfigs.GetValueOrDefault(item.VariantId)))
                .ToArray();

            // Prices are re-resolved inside the transaction and never read from the cache.
            var priced = await priceCalculator.CalculateAsync(
                request.Items
                    .Select((item, index) => new PosCartPriceLine(item.VariantId, request.OutletId, uomLines[index].BaseQuantity))
                    .ToArray(),
                readFromCache: false,
                cancellationToken);

            var priceChanges = request.Items
                .Select((item, index) => new SalePriceChange(
                    item.VariantId,
                    item.UnitPrice,
                    uomLines[index].Resolution?.Option.Price ?? priced[index].FinalPrice))
                .Where(change => Math.Abs(change.SubmittedUnitPrice - change.CurrentUnitPrice) > PriceTolerance)
                .ToArray();
            if (priceChanges.Length > 0)
                throw new SaleCheckoutConflictException(
                    SaleCheckoutErrorCodes.PriceChanged,
                    "Prices have changed since the cart was priced. Review the updated prices and try again.",
                    priceChanges);

            var taxPolicy = await checkoutSettings.GetTaxPolicyAsync(businessId, cancellationToken);
            var totals = SaleCheckoutCalculator.Calculate(
                request.Items
                    .Select((item, index) => new SaleCheckoutLineInput(
                        item.VariantId,
                        uomLines[index].BaseQuantity,
                        uomLines[index].Resolution?.BaseUnitPrice ?? priced[index].FinalPrice,
                        item.DiscountAmount,
                        priced[index].TaxRate,
                        priced[index].TaxClassification,
                        uomLines[index].Resolution?.LineGross))
                    .ToArray(),
                request.Discount,
                taxPolicy);
            if (Math.Abs(request.Tax - totals.Tax) > TaxTolerance)
                throw new SaleCheckoutConflictException(
                    SaleCheckoutErrorCodes.TaxMismatch,
                    $"Tax {request.Tax:F2} does not match the server-calculated tax {totals.Tax:F2}.",
                    new { ServerTax = totals.Tax });

            var discountEvidence = SaleDiscountAuthorizer.Authorize(
                totals,
                await checkoutSettings.GetDiscountPolicyAsync(businessId, cancellationToken),
                request.CanApplyDiscount,
                request.CanOverrideDiscountLimit,
                request.CashierId);

            var paymentPlans = SalePaymentPlanner.Plan(
                request.PaymentMethod,
                request.Payments
                    .Select(payment => new SalePaymentInput(payment.Method, payment.Amount, payment.Tendered))
                    .ToArray(),
                request.CashTendered,
                totals.TotalAmount);

            ExchangeOriginalSale? exchangeOriginal = null;
            if (request.OriginalSaleId.HasValue)
                exchangeOriginal = await LoadExchangeOriginalAsync(request, cancellationToken);

            var inventoryLines = new List<(RetailPOS.Core.Entities.Inventory Inventory, long VariantId, decimal Quantity, decimal BalanceAfter)>();
            var touchedVariants = new HashSet<long>();
            for (var index = 0; index < request.Items.Count; index++)
            {
                var item = request.Items[index];
                var baseQuantity = uomLines[index].BaseQuantity;
                var inventory = await store.FindOutletInventoryAsync(item.VariantId, request.OutletId, cancellationToken);
                if (inventory is null)
                    throw new InvalidOperationException(
                        $"No inventory found for variant ID {item.VariantId} at outlet ID {request.OutletId}");
                if (inventory.Quantity < baseQuantity)
                    throw new InvalidOperationException(
                        $"Insufficient stock for variant ID {item.VariantId}. " +
                        $"Available: {inventory.Quantity}, Requested: {baseQuantity}");

                inventory.Quantity -= baseQuantity;
                inventoryLines.Add((inventory, item.VariantId, baseQuantity, inventory.Quantity));
                touchedVariants.Add(item.VariantId);
            }

            // Packaging comes off its own stock, and a shortage blocks the whole sale (PACK-003).
            var packagingLines = new List<(int LineIndex, SalePackagingNeed Need, decimal BalanceAfter)>();
            for (var index = 0; index < uomLines.Length; index++)
            {
                foreach (var need in uomLines[index].Packaging)
                {
                    var inventory = await store.FindOutletInventoryAsync(need.VariantId, request.OutletId, cancellationToken);
                    if (inventory is null || inventory.Quantity < need.Quantity)
                        throw new UomValidationException(
                            UomErrorCodes.InsufficientPackagingStock,
                            $"Not enough packaging item {need.VariantId}. Available: {inventory?.Quantity ?? 0}, needed: {need.Quantity}.");

                    inventory.Quantity -= need.Quantity;
                    packagingLines.Add((index, need, inventory.Quantity));
                    touchedVariants.Add(need.VariantId);
                }
            }

            var totalAmount = totals.TotalAmount;
            var primaryMethod = paymentPlans.OrderByDescending(payment => payment.Amount).First().Method;
            var now = DateTime.UtcNow;
            var saleDate = request.SalesDate ?? now;
            var terminalId = await store.ResolveActiveTerminalIdAsync(
                request.OutletId,
                request.TerminalId,
                cancellationToken);
            if (!terminalId.HasValue)
                throw new SaleCheckoutValidationException(
                    SaleCheckoutErrorCodes.NoActiveTerminal,
                    $"No active terminal is configured for outlet {request.OutletId}. Add or activate a terminal in the outlet settings before selling.");

            var session = await cashSessionValidator.ValidateActiveForSaleAsync(
                request.OutletId,
                terminalId.Value,
                request.CashierId,
                cancellationToken);
            var invoice = await invoiceNumberAllocator.AllocateAsync(
                businessId,
                request.OutletId,
                session.BusinessDate.Date,
                cancellationToken);
            var sale = new Sale
            {
                SaleNumber = invoice.InvoiceNumber,
                InvoicePrefix = invoice.Prefix,
                InvoiceSequence = invoice.Sequence,
                InvoiceIssuedAtUtc = invoice.IssuedAtUtc,
                BusinessId = businessId,
                OutletId = request.OutletId,
                OriginalSaleId = request.OriginalSaleId,
                TerminalId = terminalId,
                CashSessionId = session.Id,
                BusinessDate = session.BusinessDate.Date,
                SaleDateTime = now,
                CustomerId = customerId,
                CashierId = request.CashierId,
                SalespersonUserId = request.SalespersonUserId,
                SaleDate = saleDate,
                CreatedAt = now,
                Discount = totals.InvoiceDiscount,
                Tax = totals.Tax,
                TotalAmount = totalAmount,
                PaymentMethod = primaryMethod,
                Status = "completed",
                IdempotencyKey = request.IdempotencyKey,
                IdempotencyFingerprint = fingerprint,
                Items = totals.Lines.Select((line, index) => new SaleItem
                {
                    VariantId = line.VariantId,
                    Quantity = line.Quantity,
                    UnitPrice = line.UnitPrice,
                    Subtotal = line.Subtotal,
                    DiscountAmount = line.LineDiscount,
                    // An option's price stands alone, so no pricing rule set it.
                    AppliedRuleId = uomLines[index].Resolution is null ? priced[index].AppliedRuleId : null,
                    AppliedRuleName = uomLines[index].Resolution is null ? priced[index].AppliedRuleName : null,
                    TaxableAmount = line.TaxableAmount,
                    VatRate = line.TaxRatePercent,
                    VatAmount = line.VatAmount,
                    TaxClassification = line.TaxClassification,
                    TaxCode = line.TaxRatePercent > 0m ? "VAT" : null,
                    TaxInclusive = totals.TaxInclusive,
                    PackState = uomLines[index].Form?.State,
                    PackCode = uomLines[index].Form?.PackCode,
                    BaseQtyPerPack = uomLines[index].Form?.BaseQtyPerPack
                }).ToList(),
                Payments = paymentPlans.Select(plan => new SalePayment
                {
                    Method = plan.Method,
                    Amount = plan.Amount,
                    Tendered = plan.CashTendered,
                    ChangeGiven = plan.ChangeGiven,
                    NetCashRetained = plan.NetCashRetained
                }).ToList()
            };

            var saleLines = sale.Items.ToList();
            for (var index = 0; index < uomLines.Length; index++)
                if (uomLines[index].Resolution is { } resolution)
                    saleLines[index].CaptureUomSnapshot(resolution.Snapshot);

            await store.AddSaleAsync(sale, cancellationToken);
            try
            {
                await store.SaveChangesAsync(cancellationToken);
            }
            catch (Exception exception) when (store.IsIdempotencyKeyViolation(exception))
            {
                // A concurrent request with the same key committed first: surface its result instead of an error.
                await transaction.RollbackAsync(cancellationToken);
                return await TryReplayAsync(request, fingerprint, cancellationToken) ?? throw exception;
            }

            foreach (var item in sale.Items)
            {
                var costSnapshot = await store.ResolveHistoricalUnitCostSnapshotAsync(item.VariantId, cancellationToken);
                item.CaptureHistoricalUnitCost(costSnapshot);
            }

            // Packaging cost joins COGS on the line, so the journal and the ledger take the same value (PACK-002).
            var packagingCosts = new List<decimal>();
            foreach (var (lineIndex, need, _) in packagingLines)
            {
                var unitCost = await store.ResolveHistoricalUnitCostSnapshotAsync(need.VariantId, cancellationToken);
                if (unitCost <= 0m)
                    throw new UomValidationException(
                        UomErrorCodes.PackagingCostMissing,
                        $"Packaging item {need.VariantId} has no known cost, so this sale's margin would be wrong. Receive it through a GRN first.");

                var value = Core.Money.MonetaryRounding.ToCurrency(unitCost * need.Quantity);
                saleLines[lineIndex].Components.Add(new SaleItemComponent
                {
                    ComponentVariantId = need.VariantId,
                    Quantity = need.Quantity,
                    UnitCost = unitCost,
                    Value = value
                });
                saleLines[lineIndex].PackagingCost += value;
                packagingCosts.Add(unitCost);
            }

            if (packState is not null)
            {
                for (var index = 0; index < uomLines.Length; index++)
                {
                    if (uomLines[index].Form is not { } form)
                        continue;
                    var item = saleLines[index];
                    var capitalized = 0m;
                    if (form.State == PackStates.Prepared)
                    {
                        var balances = await packState.GetBalancesForUpdateAsync(businessId, item.VariantId, request.OutletId, "outlet", cancellationToken);
                        var prepared = balances.FirstOrDefault(row =>
                            row.State == PackStates.Prepared && row.PackCode == form.PackCode && row.BaseQtyPerPack == form.BaseQtyPerPack);
                        capitalized = prepared is null ? 0m : CapitalizedValueShare.For(prepared.CapitalizedValue, prepared.PackCount, form.PackCount);
                    }

                    await packState.ApplyAsync(
                        new PackStateChange(businessId, item.VariantId, request.OutletId, "outlet", form.State, form.PackCode, form.BaseQtyPerPack,
                            -form.PackCount, -uomLines[index].BaseQuantity, -capitalized,
                            PackStateOperations.Sale, StockLedgerReferenceType.Sale, sale.Id, request.CashierId),
                        cancellationToken);
                    // The prepared pack's bottle has been in inventory since the repack; it leaves now, once.
                    item.PackagingCost += capitalized;
                }
            }

            await store.SaveChangesAsync(cancellationToken);
            if (exchangeOriginal is not null)
            {
                exchangeOriginal.Sale.ReplacementSaleId = sale.Id;
                await store.SaveChangesAsync(cancellationToken);
            }

            // D10: the journal intent and its outbox event commit with the sale; the GL journal is posted afterwards.
            await financialPostings.AppendSaleAsync(sale, cancellationToken);
            for (var index = 0; index < inventoryLines.Count; index++)
            {
                var line = inventoryLines[index];
                stockLedgerWriter.AppendSaleMovement(
                    line.VariantId,
                    request.OutletId,
                    line.Quantity,
                    line.BalanceAfter,
                    sale.Id,
                    request.CashierId,
                    saleDate,
                    saleLines[index].HistoricalUnitCost ?? 0m);
            }

            for (var index = 0; index < packagingLines.Count; index++)
                stockLedgerWriter.AppendSaleMovement(
                    packagingLines[index].Need.VariantId,
                    request.OutletId,
                    packagingLines[index].Need.Quantity,
                    packagingLines[index].BalanceAfter,
                    sale.Id,
                    request.CashierId,
                    saleDate,
                    packagingCosts[index]);

            // Legacy integer loyalty points are no longer earned at checkout: a sale must not mutate a balance that the
            // upcoming Loyalty module will own. The notice keeps the field (0) so downstream contracts stay stable.
            const int earnedPoints = 0;

            try
            {
                await store.SaveFinalChangesAsync(cancellationToken);
                await auditWriter.RecordCreatedAsync(sale, null, cancellationToken);
                if (discountEvidence is not null)
                    await auditWriter.RecordManualDiscountAsync(sale, discountEvidence, cancellationToken);
            }
            catch (SaleInventoryConcurrencyException)
            {
                await transaction.RollbackAsync(cancellationToken);
                throw new InvalidOperationException("Stock was modified by another transaction. Please retry the sale.");
            }

            if (packState is not null)
                foreach (var line in inventoryLines.Where(line => uomConfigs.GetValueOrDefault(line.VariantId)?.PackStateTrackingEnabled == true).DistinctBy(line => line.VariantId))
                    await packState.EnsureReconciledAsync(businessId, line.VariantId, request.OutletId, "outlet", line.Inventory.Quantity, cancellationToken);

            await cashSessionValidator.ValidateFinalGateAsync(session, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            financialPostings.NotifyCommitted();
            logger.SaleCreated(sale.SaleNumber, sale.Id, request.OutletId);

            foreach (var variantId in touchedVariants)
                await stockCacheInvalidator.InvalidateAsync(variantId, request.OutletId, cancellationToken);

            _ = saleCompletedPublisher.PublishAsync(
                new SaleCompletedNotice(
                    sale.Id,
                    sale.SaleNumber,
                    request.OutletId,
                    request.CashierId,
                    customerId,
                    totalAmount,
                    now,
                    earnedPoints),
                cancellationToken);

            return await store.FindCreatedSaleAsync(sale.Id, cancellationToken)
                ?? throw new KeyNotFoundException($"Sale with ID {sale.Id} not found");
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private async Task<SaleReadModel?> TryReplayAsync(
        CreateSaleCommand request,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        var existing = await store.FindByIdempotencyKeyAsync(
            request.OutletId,
            request.IdempotencyKey!,
            cancellationToken);
        if (existing is null)
            return null;

        if (existing.RequestFingerprint is not null &&
            !string.Equals(existing.RequestFingerprint, fingerprint, StringComparison.Ordinal))
            throw new SaleCheckoutConflictException(
                SaleCheckoutErrorCodes.IdempotencyPayloadMismatch,
                "This idempotency key was already used for a different sale request.");

        logger.DuplicateRequest(request.IdempotencyKey!, existing.Sale.Id);
        return existing.Sale;
    }

    private async Task<ExchangeOriginalSale> LoadExchangeOriginalAsync(
        CreateSaleCommand request,
        CancellationToken cancellationToken)
    {
        var originalSaleId = request.OriginalSaleId!.Value;
        var outletBusinessId = await store.GetOutletBusinessIdAsync(request.OutletId, cancellationToken);
        var original = await store.FindExchangeOriginalSaleForUpdateAsync(originalSaleId, cancellationToken);

        // A sale of another business is reported as missing so its existence is not disclosed.
        if (original is null || !outletBusinessId.HasValue || original.BusinessId != outletBusinessId)
            throw new KeyNotFoundException($"Original sale with ID {originalSaleId} not found");

        if (!string.Equals(original.Sale.Status, "completed", StringComparison.OrdinalIgnoreCase))
            throw new SaleCheckoutValidationException(
                SaleCheckoutErrorCodes.InvalidExchangeLink,
                $"Original sale {originalSaleId} is {original.Sale.Status} and cannot be exchanged.");

        if (original.Sale.ReplacementSaleId.HasValue)
            throw new SaleCheckoutValidationException(
                SaleCheckoutErrorCodes.InvalidExchangeLink,
                $"Original sale {originalSaleId} has already been replaced.");

        return original;
    }

    private async Task<Customer> EnsureWalkInCustomerAsync(long businessId, CancellationToken cancellationToken)
    {
        var customer = await store.FindWalkInCustomerAsync(businessId, cancellationToken);
        if (customer is null)
        {
            customer = new Customer
            {
                BusinessId = businessId,
                Name = Customer.WalkInCustomerName,
                CustomerCode = Customer.WalkInCustomerCode,
                IsSystem = true,
                IsActive = true,
                LoyaltyPoints = 0,
                CreatedAt = DateTime.UtcNow
            };
            await store.AddCustomerAsync(customer, cancellationToken);
            await store.SaveChangesAsync(cancellationToken);
            logger.WalkInCustomerCreated();
            return customer;
        }

        if (!customer.IsActive || !customer.IsSystem)
        {
            customer.IsActive = true;
            customer.IsSystem = true;
            await store.SaveChangesAsync(cancellationToken);
            logger.WalkInCustomerRepaired();
        }

        return customer;
    }
}
