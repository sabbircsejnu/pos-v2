using MediatR;
using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Application.Sales.Checkout;
using RetailPOS.Application.Sales.FinancialPostings;
using RetailPOS.Application.Sales.ReadModels;
using RetailPOS.Core.Entities;

namespace RetailPOS.Application.Sales.Commands;

public sealed record VoidSaleCommand(long SaleId, string? Reason, long ActorUserId, long? DispensingTerminalId = null)
    : IRequest<SaleReadModel>;

public sealed class VoidSaleCommandHandler(
    ISaleVoidStore store,
    ISaleReadStore saleReadStore,
    ISaleMutationOutletAuthorizer outletAuthorizer,
    ISaleFinancialPostingWriter financialPostings,
    ISaleVoidStockLedgerWriter stockLedger,
    ISaleVoidAuditWriter auditWriter,
    ISaleVoidCacheInvalidator cacheInvalidator,
    ISaleVoidLogger logger,
    ISaleCashRefundRecorder cashRefunds,
    RetailPOS.Application.Uom.IPackStateLedger? packState = null,
    RetailPOS.Application.Inventory.Abstractions.IStockLedgerWriter? rawStockLedger = null) : IRequestHandler<VoidSaleCommand, SaleReadModel>
{
    public async Task<SaleReadModel> Handle(
        VoidSaleCommand request,
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
            EnsureVoidable(sale, request.Reason);
            var previousStatus = sale.Status;

            // Lock order: dispensing session first (inside the recorder), then the sale row. A cash outflow can never
            // commit into a session that a concurrent close already finalised.
            await cashRefunds.RecordAsync(
                new SaleCashRefundRequest(
                    sale,
                    SaleCashRefundKind.Void,
                    request.ActorUserId,
                    request.DispensingTerminalId,
                    FullReversal: true,
                    RequestedAmount: null,
                    request.Reason?.Trim()),
                cancellationToken);

            if (!await store.TryMarkCompletedSaleVoidedAsync(sale.Id, cancellationToken))
            {
                var currentStatus = await store.FindCurrentSaleStatusAsync(sale.Id, cancellationToken);
                if (currentStatus is null)
                    throw new KeyNotFoundException($"Sale with ID {request.SaleId} not found");

                sale.Status = currentStatus;
                EnsureVoidable(sale, request.Reason);
                throw new InvalidOperationException("Only completed sales can be voided");
            }
            var reason = request.Reason!.Trim();
            var inventoryLedger = new List<(RetailPOS.Core.Entities.Inventory Inventory, SaleItem Item)>();

            foreach (var item in sale.Items)
            {
                var inventory = await store.FindOutletInventoryAsync(
                    item.VariantId,
                    sale.OutletId,
                    cancellationToken);
                if (inventory is null)
                    continue;

                inventory.Quantity += item.Quantity;
                inventoryLedger.Add((inventory, item));
            }

            // A void means the sale never happened: everything it consumed returns as it was, packaging included.
            foreach (var item in sale.Items.Where(item => item.PackState is not null || item.Components.Count > 0))
                await SaleUomReversal.RestoreAsync(
                    sale,
                    item,
                    item.Quantity,
                    packagingRestored: true,
                    request.ActorUserId,
                    packState,
                    rawStockLedger,
                    (variantId, token) => store.FindOutletInventoryAsync(variantId, sale.OutletId, token),
                    cancellationToken);

            foreach (var (inventory, item) in inventoryLedger)
            {
                stockLedger.AppendReturn(
                    sale,
                    item,
                    inventory.Quantity,
                    request.ActorUserId,
                    reason,
                    DateTime.UtcNow);
            }

            // Legacy points are no longer earned by a sale, so voiding one must not take points away either.
            await store.AddSaleVoidAsync(new SaleVoid
            {
                SaleId = sale.Id,
                VoidedByUserId = request.ActorUserId,
                PreviousStatus = previousStatus,
                Reason = reason,
                VoidedAt = DateTime.UtcNow
            }, cancellationToken);

            var refundLines = sale.Items
                .Select(item => new SaleRefundLine(item.Id, item.VariantId, item.Quantity))
                .ToArray();
            // D10: the reversal journal intent commits with the void and is posted asynchronously, exactly once.
            // A sale without lines never had a sale journal, so it has nothing to reverse.
            if (refundLines.Length > 0)
                await financialPostings.AppendReversalAsync(
                    sale, refundLines, SaleFinancialPostingReversalTypes.Void, request.ActorUserId, cancellationToken);

            sale.Status = "voided";
            await store.SaveChangesAsync(cancellationToken);
            await auditWriter.RecordVoidedAsync(
                sale,
                previousStatus,
                reason,
                null,
                request.ActorUserId,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            committed = true;
            financialPostings.NotifyCommitted();

            foreach (var item in sale.Items)
                await cacheInvalidator.InvalidateAsync(item.VariantId, sale.OutletId, cancellationToken);

            logger.SaleVoided(sale.Id, reason);
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

    private static void EnsureVoidable(Sale sale, string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("Void reason is required");

        if (sale.Status == "voided")
            throw new InvalidOperationException("Sale is already voided");

        if (sale.Status == "refunded")
            throw new InvalidOperationException("Cannot void a refunded sale");

        if (!string.Equals(sale.Status, "completed", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only completed sales can be voided");
    }
}
