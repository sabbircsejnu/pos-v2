namespace RetailPOS.Application.Inventory.Abstractions;

public interface IStockLedgerWriter
{
    void WriteEntry(
        long variantId,
        long locationId,
        string locationType,
        string transactionType,
        int qtyIn,
        int qtyOut,
        int balanceAfter,
        string referenceType,
        long referenceId,
        string? remarks = null,
        long? createdBy = null,
        DateTime? createdAt = null,
        decimal? unitCost = null,
        decimal? movementValue = null,
        DateTime? businessDate = null,
        long? movementSequence = null,
        long? reversalOfMovementId = null);

    void WriteEntryWithValuation(
        long variantId,
        long locationId,
        string locationType,
        string transactionType,
        int qtyIn,
        int qtyOut,
        int balanceAfter,
        string referenceType,
        long referenceId,
        string? remarks = null,
        long? createdBy = null,
        DateTime? createdAt = null,
        decimal? unitCost = null,
        decimal? movementValue = null,
        DateTime? businessDate = null,
        long? movementSequence = null,
        long? reversalOfMovementId = null);
}
