namespace RetailPOS.Application.Inventory.Abstractions;

public interface IStockLedgerWriter
{
    void WriteEntry(
        long variantId,
        long locationId,
        string locationType,
        string transactionType,
        decimal qtyIn,
        decimal qtyOut,
        decimal balanceAfter,
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
        decimal qtyIn,
        decimal qtyOut,
        decimal balanceAfter,
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

    /// <summary>
    /// Records stock that documents prove cost nothing. A supplied zero alone is not reliable: the other methods
    /// record it as unreliable, so a caller must have checked the evidence before using this one.
    /// </summary>
    void WriteVerifiedZeroCostEntry(
        long variantId,
        long locationId,
        string locationType,
        string transactionType,
        decimal qtyIn,
        decimal qtyOut,
        decimal balanceAfter,
        string referenceType,
        long referenceId,
        string? remarks = null,
        long? createdBy = null,
        DateTime? createdAt = null);
}
