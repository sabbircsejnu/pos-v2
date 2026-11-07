using RetailPOS.Application.Inventory.Costing;
namespace RetailPOS.Application.Grns.Abstractions;

public interface IGrnCompletionStockLedgerWriter
{
    void WriteEntryWithValuation(
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
        long createdBy,
        GrnLedgerCost cost);
}
