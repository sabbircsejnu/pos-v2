using RetailPOS.Core.Entities;

namespace RetailPOS.Tests.Inventory;

internal static class StockCostTestData
{
    /// <summary>A reliable receipt: the cost history a location needs before a transfer out of it can carry a value.</summary>
    public static StockLedger Receipt(
        long variantId,
        long locationId,
        string locationType,
        decimal quantity,
        decimal unitCost,
        long sequence = 1,
        long referenceId = 900,
        string referenceType = StockLedgerReferenceType.Grn,
        bool reliable = true) => new()
    {
        VariantId = variantId,
        LocationId = locationId,
        LocationType = locationType,
        TransactionType = referenceType == StockLedgerReferenceType.StockTransfer
            ? StockLedgerTransactionType.TransferIn
            : StockLedgerTransactionType.Grn,
        QtyIn = quantity,
        QtyOut = 0,
        BalanceAfter = quantity,
        UnitCost = unitCost,
        MovementValue = quantity * unitCost,
        IsCostValuationReliable = reliable,
        BusinessDate = new DateTime(2026, 1, 1),
        MovementSequence = sequence,
        ReferenceType = referenceType,
        ReferenceId = referenceId,
        CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
    };
}
