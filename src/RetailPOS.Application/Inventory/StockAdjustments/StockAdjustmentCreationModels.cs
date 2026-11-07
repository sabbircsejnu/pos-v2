namespace RetailPOS.Application.Inventory.StockAdjustments;

public sealed record StockAdjustmentCreateLine(
    long VariantId,
    decimal QuantityChange,
    string? Reason,
    string? Notes,
    long? UnitOptionId = null,
    decimal? EnteredQuantity = null,
    long? ExpectedConfigVersionId = null,
    string? PackState = null,
    string? PackCode = null,
    decimal? BaseQtyPerPack = null)
{
    /// <summary>Set by the handler once the entered unit is converted.</summary>
    public RetailPOS.Core.Entities.Uom.UnitTransactionSnapshot? Snapshot { get; init; }

    /// <summary>Copies the unit and physical form onto the stored line.</summary>
    public RetailPOS.Core.Entities.StockAdjustmentLine ApplyUomTo(RetailPOS.Core.Entities.StockAdjustmentLine line)
    {
        if (Snapshot is not null)
            line.CaptureUomSnapshot(Snapshot);
        line.PackState = PackState;
        line.PackCode = PackCode;
        line.BaseQtyPerPack = BaseQtyPerPack;
        return line;
    }
}

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
