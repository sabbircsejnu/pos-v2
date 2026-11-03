namespace RetailPOS.Application.Inventory.StockRequisitions;

public sealed record StockRequisitionFilter(
    string? Status,
    long? RequestingLocationId,
    string? RequestingLocationType,
    long? SourceLocationId,
    string? SourceLocationType,
    int PageNumber,
    int PageSize);

public sealed record StockRequisitionSearchResult(
    IReadOnlyList<StockRequisitionReadModel> StockRequisitions,
    int TotalCount,
    int PageNumber,
    int PageSize);

public sealed record StockRequisitionReadModel(
    long Id,
    string RequisitionNo,
    long RequestingLocationId,
    string RequestingLocationType,
    string RequestingLocationName,
    long SourceLocationId,
    string SourceLocationType,
    string SourceLocationName,
    long RequestedBy,
    string? RequesterName,
    DateTime RequestDate,
    string Status,
    string? Notes,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    long? UpdatedBy,
    DateTime? SubmittedAt,
    long? SubmittedBy,
    DateTime? ApprovedAt,
    long? ApprovedBy,
    DateTime? RejectedAt,
    long? RejectedBy,
    string? RejectionReason,
    DateTime? ClosedAt,
    long? ClosedBy,
    IReadOnlyList<StockRequisitionLineReadModel> Lines);

public sealed record StockRequisitionLineReadModel(
    long Id,
    long VariantId,
    string? ProductCode,
    string VariantSku,
    string ProductName,
    string? VariantAttributes,
    string? PrimaryImageThumb,
    int RequestedQuantity,
    int FulfilledQuantity,
    string? Remarks);
