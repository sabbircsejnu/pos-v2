namespace RetailPOS.Application.Inventory.StockTransfers;

public sealed record StockTransferReadScope(
    long BusinessId,
    bool CanViewAllLocations,
    string? DefaultLocationType,
    long? DefaultLocationId);

public sealed record StockTransferListFilter(
    string? Status,
    long? FromLocationId,
    long? ToLocationId);

public sealed record StockTransferSearchFilter(
    string? SearchTerm,
    string? Status,
    long? FromLocationId,
    string? FromLocationType,
    long? ToLocationId,
    string? ToLocationType,
    long? ProductVariantId,
    string? TransferType,
    DateTime? DateFrom,
    DateTime? DateTo,
    int PageNumber,
    int PageSize);

public sealed record StockTransferSearchResult(
    IReadOnlyList<StockTransferReadModel> StockTransfers,
    int TotalCount,
    int PageNumber,
    int PageSize);

public sealed record StockTransferReadModel(
    long Id,
    string? TransferNo,
    string TransferType,
    long? RelatedRequisitionId,
    long FromLocationId,
    string FromLocationType,
    string FromLocationName,
    long ToLocationId,
    string ToLocationType,
    string ToLocationName,
    DateTime TransferDate,
    string Status,
    string? Notes,
    long? ApprovedBy,
    string? ApproverName,
    DateTime? ApprovedAt,
    long? SubmittedBy,
    DateTime? SubmittedAt,
    long? DispatchedBy,
    DateTime? DispatchedAt,
    long? ReceivedBy,
    DateTime? ReceivedAt,
    long? RejectedBy,
    DateTime? RejectedAt,
    long? CancelledBy,
    DateTime? CancelledAt,
    long? UpdatedBy,
    DateTime? UpdatedAt,
    long? CreatedBy,
    string? CreatorName,
    DateTime CreatedAt,
    IReadOnlyList<StockTransferItemReadModel> Items,
    long? OriginalTransferId = null,
    long? ReturnTransferId = null,
    string? ReturnTransferNo = null);

public sealed record StockTransferItemReadModel(
    long Id,
    long VariantId,
    string? ProductCode,
    string VariantSku,
    string ProductName,
    string? VariantAttributes,
    string? PrimaryImageThumb,
    int Quantity,
    int RequestedQuantity,
    int TransferQuantity,
    int AcceptedQuantity,
    int RejectedQuantity,
    decimal UnitCost,
    string? Remarks,
    int MissingQuantity = 0,
    int DamagedQuantity = 0,
    int InTransitQuantity = 0,
    int ReturnTransitQuantity = 0);
