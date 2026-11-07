using MediatR;
using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Application.Sales.Checkout;
using RetailPOS.Application.Uom;
using RetailPOS.Core.Entities.Uom;
using RetailPOS.Application.Sales.FinancialPostings;
using RetailPOS.Application.Sales.ReadModels;
using RetailPOS.Core.Entities;

namespace RetailPOS.Application.Sales.Commands;

/// <param name="Quantity">Base units returned, never more than the line sold.</param>
/// <param name="SaleItemId">Identifies the line when a sale has several lines of one variant.</param>
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
    decimal? CashRefundAmount = null) : IRequest<SaleReadModel>;

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
    public async Task<SaleReadModel> Handle(
        RefundSaleCommand request,
        CancellationToken cancellationToken)
    {
        await using var transaction = await store.BeginTransactionAsync(cancellationToken);
        var committed = false;
        try
        {
            var sale = await store.FindSaleForUpdateAsync(request.SaleId, cancellationToken);
            if (sale is null)
                throw new KeyNotFoundException($"Sale with ID {request.SaleId} not found");

            await outletAuthorizer.AuthorizeAsync(sale.OutletId, cancellationToken);
            if (string.Equals(sale.Status, "voided", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Cannot refund a voided sale");
            if (string.Equals(sale.Status, "refunded", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Sale is already refunded");

            if (request.Items.Count == 0)
                throw new InvalidOperationException("Refund must include at least one item");

            var assessed = ResolveAssessedLines(sale, request.Items);
            var refundLines = assessed.Select(line => line.Line).ToArray();
            var previousStatus = sale.Status;

            var fullReversal = sale.Items.All(item =>
                refundLines.Where(line => line.SaleItemId == item.Id).Sum(line => line.Quantity) >= item.Quantity);
            await cashRefunds.RecordAsync(
                new SaleCashRefundRequest(
                    sale,
                    SaleCashRefundKind.Refund,
                    request.ActorUserId,
                    request.DispensingTerminalId,
                    fullReversal,
                    request.CashRefundAmount,
                    request.Reason?.Trim()),
                cancellationToken);
            if (!await store.TryMarkSaleRefundedAsync(sale.Id, previousStatus, cancellationToken))
            {
                var currentStatus = await store.FindCurrentSaleStatusAsync(sale.Id, cancellationToken);
                if (currentStatus is null)
                    throw new KeyNotFoundException($"Sale with ID {request.SaleId} not found");

                if (string.Equals(currentStatus, "voided", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Cannot refund a voided sale");
                if (string.Equals(currentStatus, "refunded", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Sale is already refunded");
                throw new InvalidOperationException("Sale status changed while the refund was being processed");
            }

            var inventoryReturns = new List<(SaleRefundLine Line, SaleItem Item, decimal BalanceAfter)>();
            var uomConfigs = uomReader is null || sale.BusinessId is not { } uomBusinessId
                ? new Dictionary<long, ResolvedVariantUom>()
                : await uomReader.GetCurrentAsync(uomBusinessId, assessed.Select(line => line.Line.VariantId).ToList(), cancellationToken);
            var finalLines = new List<SaleRefundLine>(assessed.Count);
            foreach (var (line, condition, packagingReusable) in assessed)
            {
                var saleItem = sale.Items.Single(item => item.Id == line.SaleItemId);
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
                            tracksNow);
                }

                var contentCost = resellable ? (saleItem.HistoricalUnitCost ?? 0m) * line.Quantity : 0m;
                var isAssessedLine = condition is not null || saleItem.UomUnitCode is not null;
                if (isAssessedLine)
                    saleItem.ReturnLines.Add(new SaleReturnLine
                    {
                        SaleId = sale.Id,
                        SaleItemId = saleItem.Id,
                        ReturnedQuantity = line.Quantity,
                        Condition = condition ?? ReturnConditions.Resellable,
                        RestockedQuantity = resellable ? line.Quantity : 0m,
                        PackagingReusable = packagingReusable,
                        CostRestored = contentCost + restoredPackaging,
                        ActorUserId = request.ActorUserId,
                        CreatedAt = DateTime.UtcNow
                    });
                // The original cost snapshot decides this value, never today's cost or conversion (RET-001).
                finalLines.Add(isAssessedLine ? line with { RestoredCost = contentCost + restoredPackaging } : line);
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
            await financialPostings.AppendReversalAsync(
                sale,
                finalLines,
                SaleFinancialPostingReversalTypes.Refund,
                request.ActorUserId,
                cancellationToken);

            sale.Status = "refunded";
            await store.SaveChangesAsync(cancellationToken);
            await auditWriter.RecordRefundedAsync(
                sale,
                previousStatus,
                request.Reason,
                request.Items.Sum(item => item.Quantity),
                null,
                request.ActorUserId,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            committed = true;
            financialPostings.NotifyCommitted();

            foreach (var (line, _, _) in inventoryReturns)
                await cacheInvalidator.InvalidateAsync(line.VariantId, sale.OutletId, cancellationToken);

            logger.SaleRefunded(sale.Id, request.Reason);
            return await saleReadStore.FindForReceiptAsync(sale.Id, cancellationToken)
                ?? throw new KeyNotFoundException($"Sale with ID {request.SaleId} not found");
        }
        catch
        {
            if (!committed)
                await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
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
            if (item.SaleItemId is null && candidates.Count > 1 && candidates.Any(saleItem => saleItem.UomUnitCode is not null))
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
            var condition = item.Condition?.Trim().ToLowerInvariant();
            if (condition is not (null or ReturnConditions.Resellable or ReturnConditions.NonResellable))
                throw new UomValidationException(UomErrorCodes.InvalidConfiguration, $"'{item.Condition}' is not a return condition.");
            if (item.Quantity <= 0)
                throw new InvalidOperationException(
                    $"Refund quantity must be greater than zero for variant ID {item.VariantId}");
            if (item.Quantity > saleItem.Quantity)
                throw QuantityExceedsSold(item.Quantity, saleItem.Quantity, item.VariantId);

            var line = new SaleRefundLine(saleItem.Id, item.VariantId, item.Quantity);
            resolvedLines.Add(line);
            assessed.Add((line, condition, item.PackagingReusable));
        }

        foreach (var group in resolvedLines.GroupBy(line => line.SaleItemId))
        {
            var saleItem = sale.Items.Single(item => item.Id == group.Key);
            var requestedQuantity = group.Sum(line => line.Quantity);
            if (requestedQuantity > saleItem.Quantity)
                throw QuantityExceedsSold(requestedQuantity, saleItem.Quantity, saleItem.VariantId);
        }

        return assessed;
    }

    private static InvalidOperationException QuantityExceedsSold(
        decimal requestedQuantity,
        decimal soldQuantity,
        long variantId) =>
        new(
            $"Refund quantity {UomQuantityMath.Normalize(requestedQuantity)} exceeds sold quantity {UomQuantityMath.Normalize(soldQuantity)} " +
            $"for variant ID {variantId}");
}
