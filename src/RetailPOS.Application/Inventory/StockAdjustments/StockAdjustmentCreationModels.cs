namespace RetailPOS.Application.Inventory.StockAdjustments;

public sealed record StockAdjustmentCreateLine(
    long VariantId,
    int QuantityChange,
    string? Reason,
    string? Notes);

public sealed record StockAdjustmentCreateInput(
    bool SubmitForApproval,
    bool ApproveImmediately,
    long LocationId,
    string LocationType,
    long AdjustedBy,
    IReadOnlyList<StockAdjustmentCreateLine> Items);

public sealed record StockAdjustmentUpdateInput(
    long LocationId,
    string LocationType,
    IReadOnlyList<StockAdjustmentCreateLine> Items);

public sealed record StockAdjustmentCreateContext(
    StockAdjustmentReadScope ReadScope,
    bool AllowNegativeStock);

public sealed record StockAdjustmentApprovalResult(
    long AdjustmentId,
    long? JournalEntryId);

public sealed record StockAdjustmentReversalResult(
    long AdjustmentId,
    long? OriginalJournalEntryId,
    long? ReversalJournalEntryId);
