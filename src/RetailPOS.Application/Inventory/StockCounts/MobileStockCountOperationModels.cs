namespace RetailPOS.Application.Inventory.StockCounts;

public sealed record MobileStockCountOperationInput(
    Guid ClientOperationId,
    long ClientSequence,
    long ProductVariantId,
    decimal CountedQuantity,
    string? RawBarcode,
    DateTime? ClientObservedAtUtc);

public sealed record ApplyMobileStockCountOperationsInput(
    long StockCountId,
    long BusinessId,
    long OutletId,
    long ActorUserId,
    string DeviceSessionId,
    long ExpectedRevision,
    string CorrelationId,
    IReadOnlyList<MobileStockCountOperationInput> Operations);

public sealed record MobileStockCountOperationReceiptReadModel(
    Guid ClientOperationId,
    string DeviceSessionId,
    long ClientSequence,
    long ProductVariantId,
    string OperationType,
    decimal CountedQuantity,
    string? RawBarcode,
    string PayloadHash,
    DateTime? ClientObservedAtUtc,
    DateTime ServerReceivedAtUtc,
    DateTime DecidedAtUtc,
    long? AcceptedRevision,
    long SessionRevisionAtDecision,
    string Outcome,
    string? RejectionCode,
    bool IsDuplicate = false);

public sealed record MobileStockCountOperationsBatchResult(
    long CurrentRevision,
    string? ErrorCode,
    IReadOnlyList<MobileStockCountOperationReceiptReadModel> Receipts);

public sealed record MobileStockCountOperationsPageReadModel(
    long CurrentRevision,
    long NextRevision,
    bool HasMore,
    IReadOnlyList<MobileStockCountOperationReceiptReadModel> Items);
