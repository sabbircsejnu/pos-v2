using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MediatR;
using RetailPOS.Application.Pos;
using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Application.Sales.Checkout;
using RetailPOS.Application.Uom;
using RetailPOS.Core.Entities.Uom;
using RetailPOS.Application.Sales.FinancialPostings;
using RetailPOS.Application.Sales.ReadModels;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Money;

namespace RetailPOS.Application.Sales.Commands;

/// <param name="Quantity">Base units returned, never more than the line still has to return.</param>
/// <param name="SaleItemId">Identifies the line. Required when a sale has several lines of one variant.</param>
/// <param name="Condition">Null keeps the standing behaviour: everything returns to stock.</param>
public sealed record RefundSaleLine(
    long VariantId,
    decimal Quantity,
    long? SaleItemId = null,
    string? Condition = null,
    bool PackagingReusable = false);

public sealed record RefundSaleCommand(
    long SaleId,
    IReadOnlyList<RefundSaleLine> Items,
    string? Reason,
    long ActorUserId,
    long? DispensingTerminalId = null,
    decimal? CashRefundAmount = null,
    string? IdempotencyKey = null) : IRequest<SaleReadModel>;

public static class SaleRefundErrorCodes
{
    public const string NothingLeftToReturn = "SALE_REFUND_NOTHING_LEFT";
    public const string QuantityExceedsRemaining = "SALE_REFUND_QUANTITY_EXCEEDS_REMAINING";
}

public sealed class RefundSaleCommandHandler(
    ISaleRefundStore store,
    ISaleReadStore saleReadStore,
    ISaleMutationOutletAuthorizer outletAuthorizer,
    ISaleFinancialPostingWriter financialPostings,
    ISaleRefundStockLedgerWriter stockLedger,
    ISaleRefundAuditWriter auditWriter,
    ISaleRefundCacheInvalidator cacheInvalidator,
    ISaleRefundLogger logger,
    ISaleCashRefundRecorder cashRefunds,
    IPackStateLedger? packState = null,
    RetailPOS.Application.Inventory.Abstractions.IStockLedgerWriter? rawStockLedger = null,
    IVariantUomReader? uomReader = null) : IRequestHandler<RefundSaleCommand, SaleReadModel>
{
    private const int MaxReasonLength = 500;
    private const int MaxIdempotencyKeyLength = 120;

    public async Task<SaleReadModel> Handle(
        RefundSaleCommand request,
        CancellationToken cancellationToken)
    {
        var key = string.IsNullOrWhiteSpace(request.IdempotencyKey) ? null : request.IdempotencyKey.Trim();
        if (key?.Length > MaxIdempotencyKeyLength)
            throw new InvalidOperationException($"The idempotency key can have at most {MaxIdempotencyKeyLength} characters.");
        var reason = request.Reason?.Trim();
        if (reason?.Length > MaxReasonLength)
            throw new InvalidOperationException($"The refund reason can have at most {MaxReasonLength} characters.");

        await using var transaction = await store.BeginTransactionAsync(cancellationToken);
        var committed = false;
        try
        {
            // The lock comes first, so what is read next already includes any refund that was committed while waiting.
            await store.LockSaleAsync(request.SaleId, cancellationToken);
            var sale = await store.FindSaleForUpdateAsync(request.SaleId, cancellationToken);
            if (sale is null)
                throw new KeyNotFoundException($"Sale with ID {request.SaleId} not found");

            await outletAuthorizer.AuthorizeAsync(sale.OutletId, cancellationToken);

            var fingerprint = Fingerprint(request, reason);
            if (key is not null && sale.Refunds.FirstOrDefault(refund => refund.IdempotencyKey == key) is { } earlier)
            {
                if (earlier.RequestFingerprint != fingerprint)
                    throw new SaleCheckoutConflictException(
                        SaleCheckoutErrorCodes.IdempotencyPayloadMismatch,
                        "This idempotency key was already used for a different refund.");
                await transaction.RollbackAsync(cancellationToken);
                committed = true;
                return await ReadAsync(request.SaleId, cancellationToken);
            }

            if (string.Equals(sale.Status, SaleStatuses.Voided, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Cannot refund a voided sale");
            if (string.Equals(sale.Status, SaleStatuses.Refunded, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Sale is already refunded");

            if (request.Items.Count == 0)
                throw new InvalidOperationException("Refund must include at least one item");

            // Without a key a retry cannot be told from a new refund. Such a client keeps the one refund per sale it
            // was written for, with the same answer as before; a client that sends a key can refund again.
            if (key is null && sale.Refunds.Count > 0)
                throw new InvalidOperationException("Sale is already refunded");

            var assessed = ResolveAssessedLines(sale, request.Items);
            var previousStatus = sale.Status;
            var sequence = sale.Refunds.Count == 0 ? 1 : sale.Refunds.Max(refund => refund.Sequence) + 1;
            var now = DateTime.UtcNow;

            var refund = new SaleRefund
            {
                BusinessId = sale.BusinessId,
                SaleId = sale.Id,
                Sequence = sequence,
                RefundNumber = $"{sale.SaleNumber}-R{sequence}",
                IdempotencyKey = key,
                RequestFingerprint = fingerprint,
                Reason = reason,
                ActorUserId = request.ActorUserId,
                DispensingTerminalId = request.DispensingTerminalId,
                CreatedAtUtc = now
            };
            await store.AddRefundAsync(refund, cancellationToken);

            var inventoryReturns = new List<(SaleRefundLine Line, SaleItem Item, decimal BalanceAfter)>();
            var uomConfigs = uomReader is null || sale.BusinessId is not { } uomBusinessId
                ? new Dictionary<long, ResolvedVariantUom>()
                : await uomReader.GetCurrentAsync(uomBusinessId, assessed.Select(line => line.Line.VariantId).ToList(), cancellationToken);
            var finalLines = new List<SaleRefundLine>(assessed.Count);

            // Several request lines can return one sale line in different conditions. Each takes its share after the last.
            var returnedSoFar = sale.Items.ToDictionary(item => item.Id, item => item.ReturnedQuantity);
            foreach (var (line, condition, packagingReusable) in assessed)
            {
                var saleItem = sale.Items.Single(item => item.Id == line.SaleItemId);
                var alreadyReturned = returnedSoFar[saleItem.Id];
                var resellable = condition is null or ReturnConditions.Resellable;

                // Non-resellable goods stay out of stock. Inventory is not debited, so their cost stays expensed (RET-005).
                var restoredPackaging = 0m;
                if (resellable)
                {
                    var inventory = await store.FindOutletInventoryAsync(line.VariantId, sale.OutletId, cancellationToken);
                    if (inventory is not null)
                    {
                        inventory.Quantity += line.Quantity;
                        inventoryReturns.Add((line, saleItem, inventory.Quantity));
                    }

                    var tracksNow = uomConfigs.GetValueOrDefault(line.VariantId)?.PackStateTrackingEnabled == true;
                    if (saleItem.PackState is not null || saleItem.Components.Count > 0 || tracksNow)
                        restoredPackaging = await SaleUomReversal.RestoreAsync(
                            sale,
                            saleItem,
                            line.Quantity,
                            packagingReusable,
                            request.ActorUserId,
                            packState,
                            rawStockLedger,
                            (variantId, token) => store.FindOutletInventoryAsync(variantId, sale.OutletId, token),
                            cancellationToken,
                            tracksNow,
                            alreadyReturned);
                }

                // The original cost snapshot decides this value, never today's cost or conversion (RET-001).
                var contentCost = resellable
                    ? SaleFinancialIntentFactory.ShareOfReturn(
                        MonetaryRounding.ToCurrency((saleItem.HistoricalUnitCost ?? 0m) * saleItem.Quantity),
                        alreadyReturned,
                        line.Quantity,
                        saleItem.Quantity)
                    : 0m;
                var returnLine = new SaleReturnLine
                {
                    SaleId = sale.Id,
                    SaleItemId = saleItem.Id,
                    SaleRefundId = refund.Id,
                    ReturnedQuantity = line.Quantity,
                    Condition = condition ?? ReturnConditions.Resellable,
                    RestockedQuantity = resellable ? line.Quantity : 0m,
                    PackagingReusable = packagingReusable,
                    CostRestored = contentCost + restoredPackaging,
                    NetAmount = saleItem.TaxableAmount is { } taxable
                        ? SaleFinancialIntentFactory.ShareOfReturn(taxable, alreadyReturned, line.Quantity, saleItem.Quantity)
                        : null,
                    VatAmount = saleItem.VatAmount is { } vat
                        ? SaleFinancialIntentFactory.ShareOfReturn(vat, alreadyReturned, line.Quantity, saleItem.Quantity)
                        : null,
                    ActorUserId = request.ActorUserId,
                    CreatedAt = now
                };
                saleItem.ReturnLines.Add(returnLine);
                returnedSoFar[saleItem.Id] = alreadyReturned + line.Quantity;
                finalLines.Add(line with { RestoredCost = returnLine.CostRestored });
            }

            foreach (var (line, item, balanceAfter) in inventoryReturns)
            {
                stockLedger.AppendReturn(
                    sale,
                    item,
                    line.Quantity,
                    balanceAfter,
                    request.ActorUserId,
                    request.Reason,
                    DateTime.UtcNow);
            }

            // D10: the reversal journal intent commits with the refund and is posted asynchronously, exactly once.
            var paidBackSoFar = sale.Refunds
                .Where(earlierRefund => earlierRefund.Id != refund.Id)
                .SelectMany(earlierRefund => earlierRefund.Payments)
                .GroupBy(payment => payment.SalePaymentId)
                .ToDictionary(group => group.Key, group => group.Sum(payment => payment.Amount));
            var posting = await financialPostings.AppendReversalAsync(
                sale,
                finalLines,
                SaleFinancialPostingReversalTypes.Refund,
                request.ActorUserId,
                cancellationToken,
                new SaleRefundPostingContext(refund.Id, sequence, refund.RefundNumber, paidBackSoFar));
            var snapshot = SaleFinancialIntentFactory.ReadReversalSnapshot(posting);

            refund.NetAmount = snapshot.RefundAmount;
            refund.VatAmount = snapshot.RefundVat;
            refund.TotalAmount = snapshot.RefundTotal;
            refund.CostRestored = snapshot.Cost;
            foreach (var payment in snapshot.Payments.Where(payment => payment.Amount != 0m))
                refund.Payments.Add(new SaleRefundPayment
                {
                    SalePaymentId = payment.PaymentId,
                    Method = payment.Method,
                    Amount = payment.Amount
                });

            var fullReversal = sale.Items.All(item => returnedSoFar[item.Id] >= item.Quantity);
            refund.CashAmount = await RecordCashAsync(sale, request, snapshot, fullReversal, sequence, reason, cancellationToken);

            foreach (var item in sale.Items)
                item.ReturnedQuantity = returnedSoFar[item.Id];
            sale.Status = fullReversal ? SaleStatuses.Refunded : SaleStatuses.PartiallyRefunded;
            await store.SaveChangesAsync(cancellationToken);
            await auditWriter.RecordRefundedAsync(
                sale,
                previousStatus,
                request.Reason,
                request.Items.Sum(item => item.Quantity),
                null,
                request.ActorUserId,
                cancellationToken,
                refund.RefundNumber);

            await transaction.CommitAsync(cancellationToken);
            committed = true;
            financialPostings.NotifyCommitted();

            foreach (var (line, _, _) in inventoryReturns)
                await cacheInvalidator.InvalidateAsync(line.VariantId, sale.OutletId, cancellationToken);

            logger.SaleRefunded(sale.Id, request.Reason);
            return await ReadAsync(request.SaleId, cancellationToken);
        }
        catch
        {
            if (!committed)
                await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<SaleReadModel> ReadAsync(long saleId, CancellationToken cancellationToken) =>
        await saleReadStore.FindForReceiptAsync(saleId, cancellationToken)
        ?? throw new KeyNotFoundException($"Sale with ID {saleId} not found");

    /// <summary>
    /// The drawer pays what the journal credits to cash. A stated amount can only confirm that figure. A sale without
    /// line allocations has no such figure, so it keeps stating the cash, and its journal waits for review as before.
    /// </summary>
    private async Task<decimal> RecordCashAsync(
        Sale sale,
        RefundSaleCommand request,
        SaleReversalJournalSnapshot snapshot,
        bool fullReversal,
        int sequence,
        string? reason,
        CancellationToken cancellationToken)
    {
        var requested = request.CashRefundAmount;
        if (snapshot.MissingEvidence is null)
        {
            var cashShare = snapshot.Payments
                .Where(payment => string.Equals(payment.Method, "cash", StringComparison.OrdinalIgnoreCase))
                .Sum(payment => payment.Amount);
            var heldCash = snapshot.Payments.Any(payment => string.Equals(payment.Method, "cash", StringComparison.OrdinalIgnoreCase));
            if (heldCash && requested is { } stated && stated != cashShare)
                throw new CashSessionException(
                    CashSessionErrorCodes.CashRefundPolicyUndefined,
                    $"The cash share of this refund is {cashShare:F2}, not {stated:F2}.");
            if (cashShare <= 0m)
                return 0m;
            requested = cashShare;
        }

        var result = await cashRefunds.RecordAsync(
            new SaleCashRefundRequest(
                sale,
                SaleCashRefundKind.Refund,
                request.ActorUserId,
                request.DispensingTerminalId,
                fullReversal,
                requested,
                reason,
                sequence),
            cancellationToken);
        return result?.Amount ?? 0m;
    }

    private static IReadOnlyList<(SaleRefundLine Line, string? Condition, bool PackagingReusable)> ResolveAssessedLines(
        Sale sale,
        IReadOnlyList<RefundSaleLine> items)
    {
        var resolvedLines = new List<SaleRefundLine>(items.Count);
        var assessed = new List<(SaleRefundLine, string?, bool)>(items.Count);
        foreach (var item in items)
        {
            var candidates = sale.Items.Where(saleItem => saleItem.VariantId == item.VariantId).ToList();
            if (item.SaleItemId is null && candidates.Count > 1)
                throw new UomValidationException(
                    UomErrorCodes.OptionRequired,
                    $"Variant ID {item.VariantId} was sold on several lines. Choose which line is being returned.");
            var saleItem = item.SaleItemId is { } saleItemId
                ? candidates.FirstOrDefault(candidate => candidate.Id == saleItemId)
                : candidates.FirstOrDefault();
            if (saleItem is null)
                throw new InvalidOperationException($"Variant ID {item.VariantId} not found in this sale");
            if (saleItem.UomUnitCode is null)
                UomLineResolver.EnsureLegacyWholeQuantity(item.Quantity, $"Refund quantity for variant ID {item.VariantId}");
            else
                UomQuantityMath.EnsurePrecision(item.Quantity, UomQuantityMath.StorageScale, $"Refund quantity for variant ID {item.VariantId}");
            var condition = item.Condition?.Trim().ToLowerInvariant();
            if (condition is not (null or ReturnConditions.Resellable or ReturnConditions.NonResellable))
                throw new UomValidationException(UomErrorCodes.InvalidConfiguration, $"'{item.Condition}' is not a return condition.");
            if (item.Quantity <= 0)
                throw new InvalidOperationException(
                    $"Refund quantity must be greater than zero for variant ID {item.VariantId}");

            var line = new SaleRefundLine(saleItem.Id, item.VariantId, item.Quantity);
            resolvedLines.Add(line);
            assessed.Add((line, condition, item.PackagingReusable));
        }

        foreach (var group in resolvedLines.GroupBy(line => line.SaleItemId))
        {
            var saleItem = sale.Items.Single(item => item.Id == group.Key);
            var requestedQuantity = group.Sum(line => line.Quantity);
            var remaining = saleItem.Quantity - saleItem.ReturnedQuantity;
            if (requestedQuantity > remaining)
                throw ExceedsRemaining(requestedQuantity, remaining, saleItem);
        }

        return assessed;
    }

    private static InvalidOperationException ExceedsRemaining(decimal requested, decimal remaining, SaleItem item) =>
        new(item.ReturnedQuantity > 0m
            ? $"Refund quantity {UomQuantityMath.Normalize(requested)} exceeds the {UomQuantityMath.Normalize(remaining)} left to return " +
              $"for variant ID {item.VariantId} (sold {UomQuantityMath.Normalize(item.Quantity)}, returned {UomQuantityMath.Normalize(item.ReturnedQuantity)})"
            : $"Refund quantity {UomQuantityMath.Normalize(requested)} exceeds sold quantity {UomQuantityMath.Normalize(item.Quantity)} " +
              $"for variant ID {item.VariantId}");

    /// <summary>The same request hashes the same way whatever the order of its lines or the scale of its quantities.</summary>
    private static string Fingerprint(RefundSaleCommand request, string? reason)
    {
        var lines = request.Items
            .Select(item => string.Join(
                '|',
                item.SaleItemId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                item.VariantId.ToString(CultureInfo.InvariantCulture),
                item.Quantity.ToString("0.######", CultureInfo.InvariantCulture),
                item.Condition?.Trim().ToLowerInvariant() ?? string.Empty,
                item.PackagingReusable ? "1" : "0"))
            .OrderBy(line => line, StringComparer.Ordinal);
        var cash = request.CashRefundAmount?.ToString("0.00", CultureInfo.InvariantCulture) ?? string.Empty;
        var text = $"{string.Join(';', lines)}#{cash}#{reason}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }
}
