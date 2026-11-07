using RetailPOS.Application.Inventory.Costing;
using RetailPOS.Application.PurchaseOrders.Commands;
using RetailPOS.Core.Entities;

namespace RetailPOS.Application.PurchaseOrders.Abstractions;

public interface IPurchaseAndReceiveStore
{
    Task<PurchaseAndReceiveStoreResult> ExecuteAsync(
        PurchaseAndReceiveCommand command,
        long? businessId,
        CancellationToken cancellationToken);
}

public sealed record PurchaseAndReceiveStoreResult(
    long PurchaseOrderId,
    long GrnId,
    bool IsDuplicateRequest);

public interface IPurchaseAndReceiveStockLedgerWriter
{
    void WriteEntry(
        long variantId,
        long locationId,
        string locationType,
        string transactionType,
        decimal quantityIn,
        decimal quantityOut,
        decimal balanceAfter,
        string referenceType,
        long referenceId,
        string remarks,
        long? createdBy,
        GrnLedgerCost cost);
}

public interface IPurchaseReceiptJournalPoster
{
    Task PostAsync(
        Grn grn,
        PurchaseOrder purchaseOrder,
        long businessId,
        CancellationToken cancellationToken);
}
