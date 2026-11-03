using MediatR;
using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Application.Sales.FinancialPostings;
using RetailPOS.Application.Sales.ReadModels;
using RetailPOS.Core.Entities;

namespace RetailPOS.Application.Sales.Commands;

public sealed record RefundSaleLine(long VariantId, int Quantity);

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
    ISaleCashRefundRecorder cashRefunds) : IRequestHandler<RefundSaleCommand, SaleReadModel>
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

            var refundLines = ResolveRefundLines(sale, request.Items);
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

            var inventoryReturns = new List<(SaleRefundLine Line, SaleItem Item, int BalanceAfter)>();
            foreach (var line in refundLines)
            {
                var inventory = await store.FindOutletInventoryAsync(
                    line.VariantId,
                    sale.OutletId,
                    cancellationToken);
                if (inventory is null)
                    continue;

                inventory.Quantity += line.Quantity;
                var saleItem = sale.Items.Single(item => item.Id == line.SaleItemId);
                inventoryReturns.Add((line, saleItem, inventory.Quantity));
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
                refundLines,
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

    private static IReadOnlyList<SaleRefundLine> ResolveRefundLines(
        Sale sale,
        IReadOnlyList<RefundSaleLine> items)
    {
        var resolvedLines = new List<SaleRefundLine>(items.Count);
        foreach (var item in items)
        {
            var saleItem = sale.Items.FirstOrDefault(saleItem => saleItem.VariantId == item.VariantId);
            if (saleItem is null)
                throw new InvalidOperationException($"Variant ID {item.VariantId} not found in this sale");
            if (item.Quantity <= 0)
                throw new InvalidOperationException(
                    $"Refund quantity must be greater than zero for variant ID {item.VariantId}");
            if (item.Quantity > saleItem.Quantity)
                throw QuantityExceedsSold(item.Quantity, saleItem.Quantity, item.VariantId);

            resolvedLines.Add(new SaleRefundLine(saleItem.Id, item.VariantId, item.Quantity));
        }

        foreach (var group in resolvedLines.GroupBy(line => line.SaleItemId))
        {
            var saleItem = sale.Items.Single(item => item.Id == group.Key);
            var requestedQuantity = group.Sum(line => line.Quantity);
            if (requestedQuantity > saleItem.Quantity)
                throw QuantityExceedsSold(requestedQuantity, saleItem.Quantity, saleItem.VariantId);
        }

        return resolvedLines;
    }

    private static InvalidOperationException QuantityExceedsSold(
        int requestedQuantity,
        int soldQuantity,
        long variantId) =>
        new(
            $"Refund quantity {requestedQuantity} exceeds sold quantity {soldQuantity} " +
            $"for variant ID {variantId}");
}
