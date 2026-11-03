using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Pos.Abstractions;
using RetailPOS.Application.Pos.ReadModels;
using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Application.Sales.Checkout;
using RetailPOS.Application.Sales.FinancialPostings;
using RetailPOS.Application.Sales.ReadModels;
using RetailPOS.Core.Entities;

namespace RetailPOS.Application.Sales.Commands;

/// <summary>
/// A checkout line as submitted by the client. <see cref="UnitPrice"/> is only an assertion of the price the
/// cashier saw: the server re-prices every line and rejects the sale when they differ.
/// <see cref="DiscountAmount"/> is a manual line discount; pricing-rule discounts are already part of the server price.
/// <see cref="AppliedRuleId"/>/<see cref="AppliedRuleName"/> are ignored; the server records the rule it applied.
/// </summary>
public sealed record CreateSaleLine(
    long VariantId,
    int Quantity,
    decimal UnitPrice,
    decimal DiscountAmount,
    long? AppliedRuleId,
    string? AppliedRuleName);

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
    ISaleCheckoutSettingsProvider checkoutSettings)
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

            // Prices are re-resolved inside the transaction and never read from the cache.
            var priced = await priceCalculator.CalculateAsync(
                request.Items
                    .Select(item => new PosCartPriceLine(item.VariantId, request.OutletId, item.Quantity))
                    .ToArray(),
                readFromCache: false,
                cancellationToken);

            var priceChanges = request.Items
                .Select((item, index) => new SalePriceChange(item.VariantId, item.UnitPrice, priced[index].FinalPrice))
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
                        item.Quantity,
                        priced[index].FinalPrice,
                        item.DiscountAmount,
                        priced[index].TaxRate,
                        priced[index].TaxClassification))
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

            var inventoryLines = new List<(RetailPOS.Core.Entities.Inventory Inventory, CreateSaleLine Item)>();
            foreach (var item in request.Items)
            {
                var inventory = await store.FindOutletInventoryAsync(item.VariantId, request.OutletId, cancellationToken);
                if (inventory is null)
                    throw new InvalidOperationException(
                        $"No inventory found for variant ID {item.VariantId} at outlet ID {request.OutletId}");
                if (inventory.Quantity < item.Quantity)
                    throw new InvalidOperationException(
                        $"Insufficient stock for variant ID {item.VariantId}. " +
                        $"Available: {inventory.Quantity}, Requested: {item.Quantity}");

                inventory.Quantity -= item.Quantity;
                inventoryLines.Add((inventory, item));
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
                    AppliedRuleId = priced[index].AppliedRuleId,
                    AppliedRuleName = priced[index].AppliedRuleName,
                    TaxableAmount = line.TaxableAmount,
                    VatRate = line.TaxRatePercent,
                    VatAmount = line.VatAmount,
                    TaxClassification = line.TaxClassification,
                    TaxCode = line.TaxRatePercent > 0m ? "VAT" : null,
                    TaxInclusive = totals.TaxInclusive
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

            await store.SaveChangesAsync(cancellationToken);
            if (exchangeOriginal is not null)
            {
                exchangeOriginal.Sale.ReplacementSaleId = sale.Id;
                await store.SaveChangesAsync(cancellationToken);
            }

            // D10: the journal intent and its outbox event commit with the sale; the GL journal is posted afterwards.
            await financialPostings.AppendSaleAsync(sale, cancellationToken);
            foreach (var (inventory, item) in inventoryLines)
            {
                var saleItem = sale.Items.First(line => line.VariantId == item.VariantId);
                stockLedgerWriter.AppendSaleMovement(
                    item.VariantId,
                    request.OutletId,
                    item.Quantity,
                    inventory.Quantity,
                    sale.Id,
                    request.CashierId,
                    saleDate,
                    saleItem.HistoricalUnitCost ?? 0m);
            }

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

            await cashSessionValidator.ValidateFinalGateAsync(session, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            financialPostings.NotifyCommitted();
            logger.SaleCreated(sale.SaleNumber, sale.Id, request.OutletId);

            foreach (var item in request.Items)
                await stockCacheInvalidator.InvalidateAsync(item.VariantId, request.OutletId, cancellationToken);

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
