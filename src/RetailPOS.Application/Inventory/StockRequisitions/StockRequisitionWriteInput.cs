namespace RetailPOS.Application.Inventory.StockRequisitions;

public sealed record StockRequisitionWriteInput(
    long RequestingLocationId,
    string RequestingLocationType,
    long SourceLocationId,
    string SourceLocationType,
    string? Notes,
    IReadOnlyList<StockRequisitionWriteLineInput> Lines);

public sealed record StockRequisitionWriteLineInput(
    long VariantId,
    decimal RequestedQuantity,
    string? Remarks);
