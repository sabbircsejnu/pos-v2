namespace RetailPOS.Application.Inventory.StockAdjustments;

public sealed record StockAdjustmentLocationScope(long Id, string Type);

public sealed record StockAdjustmentReadScope(
    long BusinessId,
    IReadOnlyList<StockAdjustmentLocationScope> Locations);

public sealed record StockAdjustmentLineReadModel(
    long Id,
    long VariantId,
    string ProductName,
    string VariantSku,
    string ProductCode,
    string Barcode,
    decimal PreviousQuantity,
    decimal QuantityChange,
    decimal NewQuantity,
    string Reason,
    string? Notes);

public sealed record StockAdjustmentReadModel(
    long Id,
    string AdjustmentNumber,
    string Status,
    long LocationId,
    string LocationType,
    long? SourceStockCountId,
    string LocationName,
    long VariantId,
    string VariantSku,
    string ProductName,
    decimal PreviousQuantity,
    decimal QuantityChange,
    decimal NewQuantity,
    string Reason,
    string? Notes,
    int LineCount,
    decimal TotalIncrease,
    decimal TotalDecrease,
    decimal NetQuantityChange,
    IReadOnlyList<StockAdjustmentLineReadModel> Lines,
    long AdjustedBy,
    string AdjusterName,
    DateTime AdjustmentDate,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? SubmittedAt,
    long? ApprovedBy,
    string? ApprovedByName,
    DateTime? ApprovedAt,
    long? RejectedBy,
    string? RejectedByName,
    DateTime? RejectedAt,
    string? RejectionReason,
    long? CancelledBy,
    string? CancelledByName,
    DateTime? CancelledAt);

public sealed record StockAdjustmentListFilter(
    long? LocationId = null,
    string? LocationType = null,
    long? VariantId = null,
    string? Status = null);

public sealed record StockAdjustmentSearchFilter(
    long? LocationId,
    string? LocationType,
    long? VariantId,
    string? Status,
    DateTime? StartDate,
    DateTime? EndDate,
    int PageNumber,
    int PageSize);

public sealed record StockAdjustmentSearchReadModel(
    IReadOnlyList<StockAdjustmentReadModel> StockAdjustments,
    int TotalCount,
    int PageNumber,
    int PageSize);
