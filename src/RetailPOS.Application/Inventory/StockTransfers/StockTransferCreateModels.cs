namespace RetailPOS.Application.Inventory.StockTransfers;

public sealed record StockTransferCreateItem(
    long VariantId,
    decimal Quantity,
    decimal? RequestedQuantity,
    decimal? TransferQuantity,
    decimal UnitCost,
    string? Remarks,
    long? UnitOptionId = null,
    decimal? EnteredQuantity = null,
    long? ExpectedConfigVersionId = null,
    IReadOnlyList<RetailPOS.Application.Uom.TransferAllocationInput>? Allocations = null)
{
    /// <summary>Set by the handler once the entered unit is converted.</summary>
    public RetailPOS.Core.Entities.Uom.UnitTransactionSnapshot? Snapshot { get; init; }
}

public sealed record StockTransferCreateInput(
    string? TransferType,
    long? RelatedRequisitionId,
    long FromLocationId,
    string FromLocationType,
    long ToLocationId,
    string ToLocationType,
    DateTime TransferDate,
    string? Notes,
    IReadOnlyList<StockTransferCreateItem> Items);
