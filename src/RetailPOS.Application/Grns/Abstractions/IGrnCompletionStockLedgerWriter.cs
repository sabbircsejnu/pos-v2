using RetailPOS.Application.Inventory.Costing;
namespace RetailPOS.Application.Grns.Abstractions;

public interface IGrnCompletionStockLedgerWriter
{
    void WriteEntryWithValuation(
        long variantId,
        long locationId,
        string locationType,
        string transactionType,
        int quantityIn,
        int quantityOut,
        int balanceAfter,
        string referenceType,
        long referenceId,
        string remarks,
        long createdBy,
        GrnLedgerCost cost);
}
