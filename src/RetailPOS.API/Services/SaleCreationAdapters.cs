using MediatR;
using RetailPOS.API.DTOs.Accounting;
using RetailPOS.API.Services;
using RetailPOS.Application.AccountingJournals.Abstractions;
using RetailPOS.Application.AccountingJournals.Commands;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Application.Sales.Checkout;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Core.Money;
using RetailPOS.Infrastructure.Audit;

namespace RetailPOS.API.Services;

public sealed class SaleMutationOutletAuthorizerAdapter(IUserOutletAccessService outletAccess)
    : ISaleMutationOutletAuthorizer
{
    public async Task AuthorizeAsync(long outletId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await outletAccess.ResolveAndAuthorizeLocationAsync(outletId, "outlet");
    }
}

internal static class SaleJournalInputFactory
{
    public static ManualJournalDraftInput Create(
        string journalType,
        DateTime postingDate,
        DateTime? businessDate,
        string referenceType,
        string referenceId,
        string referenceNumber,
        string description,
        long outletId,
        long? terminalId,
        long? cashSessionId,
        IReadOnlyList<CreateJournalEntryLineDto> lines,
        DateTime? transactionDate = null) =>
        new(
            journalType,
            transactionDate ?? postingDate,
            postingDate,
            businessDate,
            referenceType,
            referenceId,
            referenceNumber,
            description,
            outletId,
            null,
            terminalId,
            cashSessionId,
            lines.Select(line => new ManualJournalDraftLineInput(
                line.AccountId,
                line.Description,
                line.DebitAmount,
                line.CreditAmount,
                line.OutletId,
                line.WarehouseId,
                line.SupplierId,
                line.CustomerId,
                line.PaymentMethodId,
                line.CostCenterId)).ToArray());
}

public sealed class SaleCreationAuditWriter(IAuditService audit) : ISaleCreationAuditWriter
{
    public Task RecordCreatedAsync(Sale sale, long? journalEntryId, CancellationToken cancellationToken) =>
        audit.RecordAsync(new AuditEventInput
        {
            ActionType = AuditActionType.Create,
            Module = AuditModule.Sales,
            Summary = $"Sale completed: {sale.SaleNumber}",
            PrimaryEntity = ("Sale", sale.Id.ToString()),
            Metadata = new Dictionary<string, object?>
            {
                ["saleId"] = sale.Id,
                ["saleNumber"] = sale.SaleNumber,
                ["originalSaleId"] = sale.OriginalSaleId,
                ["replacementSaleId"] = sale.ReplacementSaleId,
                ["outletId"] = sale.OutletId,
                ["terminalId"] = sale.TerminalId,
                ["cashSessionId"] = sale.CashSessionId,
                ["customerId"] = sale.CustomerId,
                ["cashierId"] = sale.CashierId,
                ["actorUserId"] = sale.CashierId,
                ["totalAmount"] = sale.TotalAmount,
                ["paymentMethod"] = sale.PaymentMethod,
                ["journalEntryId"] = journalEntryId
            },
            Entities =
            [
                new AuditEntityInput(
                    "Sale",
                    sale.Id.ToString(),
                    AuditOperationType.Insert,
                    [
                        ("Status", null, sale.Status),
                        ("OriginalSaleId", null, sale.OriginalSaleId),
                        ("TotalAmount", null, sale.TotalAmount),
                        ("PaymentMethod", null, sale.PaymentMethod),
                        ("ItemCount", null, sale.Items.Count),
                        ("JournalEntryId", null, journalEntryId)
                    ])
            ]
        },
            cancellationToken);

    public Task RecordManualDiscountAsync(
        Sale sale,
        ManualDiscountEvidence evidence,
        CancellationToken cancellationToken) =>
        audit.RecordAsync(new AuditEventInput
        {
            ActionType = AuditActionType.Adjustment,
            Module = AuditModule.Sales,
            Summary = evidence.OverrideUsed
                ? $"Manual discount above the limit applied with override on sale {sale.SaleNumber}"
                : $"Manual discount applied on sale {sale.SaleNumber}",
            PrimaryEntity = ("Sale", sale.Id.ToString()),
            Metadata = new Dictionary<string, object?>
            {
                ["saleId"] = sale.Id,
                ["saleNumber"] = sale.SaleNumber,
                ["outletId"] = sale.OutletId,
                ["cashierId"] = evidence.CashierId,
                ["actorUserId"] = evidence.CashierId,
                ["discountScope"] = evidence.Scope,
                ["lineDiscountTotal"] = evidence.LineDiscountTotal,
                ["invoiceDiscount"] = evidence.InvoiceDiscount,
                ["effectiveDiscountPercent"] = evidence.EffectivePercent,
                ["limitPercent"] = evidence.LimitPercent,
                ["overrideUsed"] = evidence.OverrideUsed
            }
        },
            cancellationToken);
}

public sealed class SaleStockLedgerWriter(IStockLedgerWriter stockLedger) : ISaleStockLedgerWriter
{
    public void AppendSaleMovement(
        long variantId,
        long outletId,
        decimal quantity,
        decimal balanceAfter,
        long saleId,
        long cashierId,
        DateTime saleDate,
        decimal unitCost) =>
        stockLedger.WriteEntryWithValuation(
            variantId: variantId,
            locationId: outletId,
            locationType: "outlet",
            transactionType: StockLedgerTransactionType.Sale,
            qtyIn: 0,
            qtyOut: quantity,
            balanceAfter: balanceAfter,
            referenceType: StockLedgerReferenceType.Sale,
            referenceId: saleId,
            createdBy: cashierId,
            createdAt: saleDate,
            unitCost: unitCost);
}

public sealed class SaleStockCacheInvalidator(IPosCacheService cache) : ISaleStockCacheInvalidator
{
    public Task InvalidateAsync(long variantId, long outletId, CancellationToken cancellationToken) =>
        cache.InvalidateStockAsync(variantId, outletId);
}

public sealed class SaleCompletedPublisher(ISaleEventPublisher publisher) : ISaleCompletedPublisher
{
    public Task PublishAsync(SaleCompletedNotice sale, CancellationToken cancellationToken) =>
        publisher.PublishSaleCompletedAsync(new SaleCompletedEvent
        {
            SaleId = sale.SaleId,
            SaleNumber = sale.SaleNumber,
            OutletId = sale.OutletId,
            CashierId = sale.CashierId,
            CustomerId = sale.CustomerId,
            TotalAmount = sale.TotalAmount,
            CompletedAt = sale.CompletedAt,
            LoyaltyPointsAwarded = sale.LoyaltyPointsAwarded
        });
}

public sealed class SaleCreationLogger(ILogger<SaleCreationLogger> logger) : ISaleCreationLogger
{
    public void DuplicateRequest(string idempotencyKey, long saleId) =>
        logger.LogInformation(
            "Duplicate submission detected for IdempotencyKey={Key}, returning existing Sale {SaleId}",
            idempotencyKey,
            saleId);

    public void SaleCreated(string saleNumber, long saleId, long outletId) =>
        logger.LogInformation(
            "Sale {SaleNumber} (ID={SaleId}) created for outlet {OutletId}",
            saleNumber,
            saleId,
            outletId);

    public void WalkInCustomerCreated() =>
        logger.LogWarning("WALKIN customer was missing and has been auto-created by the Sale application flow.");

    public void WalkInCustomerRepaired() =>
        logger.LogWarning("WALKIN customer existed but was corrected to active system customer by the Sale application flow.");
}

public sealed class SaleRefundStockLedgerWriter(IStockLedgerWriter stockLedger)
    : ISaleRefundStockLedgerWriter
{
    public void AppendReturn(
        Sale sale,
        SaleItem item,
        decimal quantity,
        decimal balanceAfter,
        long actorUserId,
        string? reason,
        DateTime createdAt)
    {
        if (!item.HistoricalUnitCost.HasValue)
            throw new InvalidOperationException(
                $"Historical unit cost is missing for sale line {item.Id}. Legacy sales without a historical snapshot cannot be reversed using current cost.");

        var historicalUnitCost = item.HistoricalUnitCost.Value;
        var reversalCost = Math.Round(historicalUnitCost * quantity, 4);
        var remarks = $"Refund reversal using historical unit cost {historicalUnitCost:0.0000} x {quantity} = {reversalCost:0.0000}";
        if (!string.IsNullOrWhiteSpace(reason))
            remarks = $"{remarks}; reason: {reason}";

        stockLedger.WriteEntryWithValuation(
            variantId: item.VariantId,
            locationId: sale.OutletId,
            locationType: "outlet",
            transactionType: StockLedgerTransactionType.Return,
            qtyIn: quantity,
            qtyOut: 0,
            balanceAfter: balanceAfter,
            referenceType: StockLedgerReferenceType.Sale,
            referenceId: sale.Id,
            remarks: remarks,
            createdBy: actorUserId,
            createdAt: createdAt,
            unitCost: historicalUnitCost);
    }
}

public sealed class SaleRefundAuditWriter(IAuditService audit) : ISaleRefundAuditWriter
{
    public Task RecordRefundedAsync(
        Sale sale,
        string previousStatus,
        string? reason,
        decimal refundQuantity,
        long? journalEntryId,
        long actorUserId,
        CancellationToken cancellationToken,
        string? refundNumber = null) =>
        audit.RecordAsync(new AuditEventInput
        {
            ActionType = AuditActionType.Reverse,
            Module = AuditModule.Sales,
            Summary = $"Sale refunded; accounting reversal queued: {refundNumber ?? sale.SaleNumber}",
            PrimaryEntity = ("Sale", sale.Id.ToString()),
            Metadata = new Dictionary<string, object?>
            {
                ["refundNumber"] = refundNumber,
                ["saleId"] = sale.Id,
                ["saleNumber"] = sale.SaleNumber,
                ["originalSaleId"] = sale.OriginalSaleId,
                ["replacementSaleId"] = sale.ReplacementSaleId,
                ["outletId"] = sale.OutletId,
                ["terminalId"] = sale.TerminalId,
                ["cashSessionId"] = sale.CashSessionId,
                ["customerId"] = sale.CustomerId,
                ["cashierId"] = sale.CashierId,
                ["actorUserId"] = actorUserId,
                ["totalAmount"] = sale.TotalAmount,
                ["paymentMethod"] = sale.PaymentMethod,
                ["journalEntryId"] = journalEntryId
            },
            Entities =
            [
                new AuditEntityInput(
                    "Sale",
                    sale.Id.ToString(),
                    AuditOperationType.Update,
                    [
                        ("Status", previousStatus, sale.Status),
                        ("Reason", null, reason),
                        ("RefundQuantity", null, refundQuantity),
                        ("JournalEntryId", null, journalEntryId)
                    ])
            ]
        },
            cancellationToken);
}

public sealed class SaleRefundCacheInvalidator(IPosCacheService cache) : ISaleRefundCacheInvalidator
{
    public Task InvalidateAsync(long variantId, long outletId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return cache.InvalidateStockAsync(variantId, outletId);
    }
}

public sealed class SaleRefundLogger(ILogger<SaleRefundLogger> logger) : ISaleRefundLogger
{
    public void SaleRefunded(long saleId, string? reason) =>
        logger.LogInformation("Sale {SaleId} refunded. Reason: {Reason}", saleId, reason);
}

public sealed class SaleVoidStockLedgerWriter(IStockLedgerWriter stockLedger) : ISaleVoidStockLedgerWriter
{
    public void AppendReturn(
        Sale sale,
        SaleItem item,
        decimal balanceAfter,
        long actorUserId,
        string reason,
        DateTime createdAt)
    {
        if (!item.HistoricalUnitCost.HasValue)
            throw new InvalidOperationException(
                $"Historical unit cost is missing for sale line {item.Id}. Legacy sales without a historical snapshot cannot be reversed using current cost.");

        var historicalUnitCost = item.HistoricalUnitCost.Value;
        var reversalCost = Math.Round(historicalUnitCost * item.Quantity, 4);
        var remarks = $"Void reversal using historical unit cost {historicalUnitCost:0.0000} x {item.Quantity} = {reversalCost:0.0000}; reason: {reason}";
        stockLedger.WriteEntryWithValuation(
            variantId: item.VariantId,
            locationId: sale.OutletId,
            locationType: "outlet",
            transactionType: StockLedgerTransactionType.Return,
            qtyIn: item.Quantity,
            qtyOut: 0,
            balanceAfter: balanceAfter,
            referenceType: StockLedgerReferenceType.Sale,
            referenceId: sale.Id,
            remarks: remarks,
            createdBy: actorUserId,
            createdAt: createdAt,
            unitCost: item.HistoricalUnitCost.Value);
    }
}

public sealed class SaleVoidAuditWriter(IAuditService audit) : ISaleVoidAuditWriter
{
    public Task RecordVoidedAsync(
        Sale sale,
        string previousStatus,
        string reason,
        long? journalEntryId,
        long actorUserId,
        CancellationToken cancellationToken) =>
        audit.RecordAsync(new AuditEventInput
        {
            ActionType = AuditActionType.Reverse,
            Module = AuditModule.Sales,
            Summary = $"Sale voided; accounting reversal queued: {sale.SaleNumber}",
            PrimaryEntity = ("Sale", sale.Id.ToString()),
            Metadata = new Dictionary<string, object?>
            {
                ["saleId"] = sale.Id,
                ["saleNumber"] = sale.SaleNumber,
                ["originalSaleId"] = sale.OriginalSaleId,
                ["replacementSaleId"] = sale.ReplacementSaleId,
                ["outletId"] = sale.OutletId,
                ["terminalId"] = sale.TerminalId,
                ["cashSessionId"] = sale.CashSessionId,
                ["customerId"] = sale.CustomerId,
                ["cashierId"] = sale.CashierId,
                ["actorUserId"] = actorUserId,
                ["totalAmount"] = sale.TotalAmount,
                ["paymentMethod"] = sale.PaymentMethod,
                ["journalEntryId"] = journalEntryId
            },
            Entities =
            [
                new AuditEntityInput(
                    "Sale",
                    sale.Id.ToString(),
                    AuditOperationType.Update,
                    [
                        ("Status", previousStatus, sale.Status),
                        ("Reason", null, reason),
                        ("JournalEntryId", null, journalEntryId)
                    ])
            ]
        });
}

public sealed class SaleVoidCacheInvalidator(IPosCacheService cache) : ISaleVoidCacheInvalidator
{
    public Task InvalidateAsync(long variantId, long outletId, CancellationToken cancellationToken) =>
        cache.InvalidateStockAsync(variantId, outletId);
}

public sealed class SaleVoidLogger(ILogger<SaleVoidLogger> logger) : ISaleVoidLogger
{
    public void SaleVoided(long saleId, string reason) =>
        logger.LogInformation("Sale {SaleId} voided. Reason: {Reason}", saleId, reason);
}
