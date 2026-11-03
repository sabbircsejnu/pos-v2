namespace RetailPOS.Application.Inventory.StockCounts;

public sealed record StockCountReadScope(
    long? BusinessId,
    long? LocationId,
    string? LocationType,
    IReadOnlyList<long> AuthorizedOutletIds,
    IReadOnlyList<long> AuthorizedWarehouseIds);

public sealed record StockCountSearchFilter(
    string? Search,
    DateTime? DateFrom,
    DateTime? DateTo,
    string? Status,
    int PageNumber,
    int PageSize);

public sealed record StockCountSearchItemReadModel(
    long Id,
    string StockCountNo,
    long BusinessId,
    long LocationId,
    string LocationType,
    string LocationName,
    DateTime StockCountDate,
    string Status,
    string? Remarks,
    int TotalItems,
    long CreatedBy,
    string CreatedByName,
    DateTime CreatedAt,
    long? SubmittedBy,
    DateTime? SubmittedAt,
    long? ApprovedBy,
    DateTime? ApprovedAt,
    long? RejectedBy,
    DateTime? RejectedAt,
    string? RejectionReason,
    bool HasPostGenerationMovements,
    int PostGenerationMovementCount,
    DateTime? LastPostGenerationMovementAt);

public sealed record StockCountSearchResultReadModel(
    IReadOnlyList<StockCountSearchItemReadModel> StockCounts,
    int TotalCount,
    int PageNumber,
    int PageSize);

public sealed record StockCountLineReadModel(
    long Id,
    long ProductId,
    long VariantId,
    string ProductName,
    string ProductCode,
    string VariantName,
    decimal CurrentStock,
    decimal? PhysicalStock,
    decimal? Difference,
    string? Remarks);

public sealed record StockCountDetailReadModel(
    long Id,
    string StockCountNo,
    long BusinessId,
    string BusinessName,
    long LocationId,
    string LocationType,
    string LocationName,
    DateTime StockCountDate,
    string Status,
    string? Remarks,
    int TotalItems,
    long CreatedBy,
    string CreatedByName,
    DateTime CreatedAt,
    long? SubmittedBy,
    DateTime? SubmittedAt,
    long? ApprovedBy,
    DateTime? ApprovedAt,
    long? RejectedBy,
    DateTime? RejectedAt,
    string? RejectionReason,
    bool HasPostGenerationMovements,
    int PostGenerationMovementCount,
    DateTime? LastPostGenerationMovementAt,
    IReadOnlyList<StockCountLineReadModel> Lines,
    StockCountAdjustmentJobReadModel? AdjustmentJob = null);
