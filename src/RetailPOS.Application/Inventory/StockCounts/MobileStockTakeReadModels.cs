namespace RetailPOS.Application.Inventory.StockCounts;

public sealed record MobileStockTakeSummaryReadModel(
    long Id,
    string Reference,
    long OutletId,
    string OutletName,
    DateTime StockCountDate,
    string Status,
    string? Remarks,
    int TotalItems,
    DateTime CreatedAt,
    DateTime? SubmittedAt,
    long CountRevision,
    string BaselineConsistencyStatus,
    DateTime? BaselineInventoryReadStartedAtUtc,
    DateTime? BaselineInventoryReadCompletedAtUtc,
    string MovementReconciliationStatus);

public sealed record MobileStockTakePageReadModel(
    IReadOnlyList<MobileStockTakeSummaryReadModel> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record MobileStockTakeLineReadModel(
    long Id,
    long ProductId,
    long VariantId,
    string ProductName,
    string ProductCode,
    string VariantName,
    decimal? CountedQuantity,
    string? Remarks);

public sealed record MobileStockTakeLinePageReadModel(
    IReadOnlyList<MobileStockTakeLineReadModel> Items,
    int Page,
    int PageSize,
    int TotalCount);
