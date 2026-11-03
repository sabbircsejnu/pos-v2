namespace RetailPOS.API.DTOs.Mobile;

public sealed record MobileStockTakeCreateRequest(
    long OutletId,
    DateOnly StockCountDate,
    string? Remarks);

public sealed record MobileStockTakePageDto(
    IReadOnlyList<MobileStockTakeSessionDto> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record MobileStockTakeSessionDto(
    long Id,
    string Reference,
    MobileStockTakeScopeDto Scope,
    DateOnly StockCountDate,
    string Status,
    string? Remarks,
    int TotalItems,
    DateTime CreatedAt,
    DateTime? SubmittedAt,
    long? Revision,
    string BaselineConsistencyStatus,
    DateTime? BaselineInventoryReadStartedAtUtc,
    DateTime? BaselineInventoryReadCompletedAtUtc,
    string MovementReconciliationStatus,
    IReadOnlyList<string> PermittedActions);

public sealed record MobileStockTakeOperationRequest(
    Guid ClientOperationId,
    long ClientSequence,
    long ProductVariantId,
    decimal CountedQuantity,
    string? RawBarcode,
    DateTimeOffset? ClientObservedAt);

public sealed record MobileStockTakeOperationsBatchRequest(
    string DeviceSessionId,
    long ExpectedRevision,
    IReadOnlyList<MobileStockTakeOperationRequest> Operations);

public sealed record MobileStockTakeOperationReceiptDto(
    Guid ClientOperationId,
    string DeviceSessionId,
    long ClientSequence,
    long ProductVariantId,
    string OperationType,
    decimal CountedQuantity,
    string? RawBarcode,
    DateTime? ClientObservedAtUtc,
    DateTime ServerReceivedAtUtc,
    DateTime DecidedAtUtc,
    long? AcceptedRevision,
    long SessionRevisionAtDecision,
    string Outcome,
    string? RejectionCode,
    bool IsDuplicate);

public sealed record MobileStockTakeOperationsBatchDto(
    long CurrentRevision,
    string? ErrorCode,
    IReadOnlyList<MobileStockTakeOperationReceiptDto> Receipts);

public sealed record MobileStockTakeOperationsPageDto(
    long CurrentRevision,
    long NextRevision,
    bool HasMore,
    IReadOnlyList<MobileStockTakeOperationReceiptDto> Items);

public sealed record MobileStockTakeScopeDto(
    string LocationType,
    long LocationId,
    string LocationName);

public sealed record MobileStockTakeLineDto(
    long Id,
    long ProductId,
    long VariantId,
    string ProductName,
    string ProductCode,
    string VariantName,
    decimal? CountedQuantity,
    string? Remarks);

public sealed record MobileStockTakeLinePageDto(
    IReadOnlyList<MobileStockTakeLineDto> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record MobileStockTakeSubmissionRequest(long AcceptedRevision);

/// <summary>
/// Durable submission job state. <c>ErpSubmitted</c> means only that the existing ERP StockCount submission
/// succeeded; it is not approval, reconciliation or inventory posting.
/// </summary>
public sealed record MobileStockTakeSubmissionDto(
    Guid JobId,
    long StockTakeId,
    long AcceptedRevision,
    string Status,
    bool IsTerminal,
    int AttemptCount,
    DateTime? NextAttemptAtUtc,
    string? FailureCode,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? CompletedAtUtc,
    string SessionStatus,
    string MovementReconciliationStatus,
    string StatusUrl,
    bool IsReplay = false);

public sealed record MobileStockTakeLatestSubmissionDto(
    Guid JobId,
    string Status,
    bool IsTerminal,
    int AttemptCount,
    string? FailureCode,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? CompletedAtUtc);

public sealed record MobileStockTakeHistoryItemDto(
    long Id,
    string Reference,
    MobileStockTakeScopeDto Scope,
    DateOnly StockCountDate,
    string ErpStatus,
    string Stage,
    long Revision,
    int TotalItems,
    DateTime CreatedAtUtc,
    DateTime? SubmittedAtUtc,
    DateTime? ApprovedAtUtc,
    DateTime? RejectedAtUtc,
    string ErpMovementReconciliationStatus,
    MobileStockTakeLatestSubmissionDto? LatestSubmission);

public sealed record MobileStockTakeHistoryPageDto(
    IReadOnlyList<MobileStockTakeHistoryItemDto> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record MobileStockTakeReconciliationReasonDto(string Code, int AffectedLineCount);

/// <summary>Movement row counts only; no quantities, expected stock or variance.</summary>
public sealed record MobileStockTakeMovementSummaryDto(
    int SaleCount,
    int ReturnCount,
    int TransferCount,
    int AdjustmentCount,
    int GoodsReceiptCount,
    int OtherCount,
    int PostCountMovementCount);

public sealed record MobileStockTakeReconciliationDto(
    string ErpStatus,
    string EvidenceStatus,
    bool ResolvesErpReconciliation,
    IReadOnlyList<MobileStockTakeReconciliationReasonDto> Reasons,
    MobileStockTakeMovementSummaryDto Movements,
    int EvaluatedLineCount,
    DateTime EvaluatedAtUtc);

public sealed record MobileStockTakeApprovalDto(string State, DateTime? DecidedAtUtc, bool HasRejectionReason);

public sealed record MobileStockTakeAdjustmentJobDto(
    Guid JobId,
    string Status,
    bool IsTerminal,
    string? FailureCode,
    string? EvidenceStatus,
    IReadOnlyList<string> BlockedReasonCodes,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc);

public sealed record MobileStockTakeInventoryAdjustmentDto(
    string State,
    bool IsInventoryPosted,
    bool DraftBlockedByErpReconciliation,
    MobileStockTakeAdjustmentJobDto? Job = null);

public sealed record MobileStockTakeResultsStatusDto(string Availability, IReadOnlyList<string> Reasons);

/// <summary>
/// Separates the four independent facts of a Stock Take: submission, reconciliation, approval and inventory
/// adjustment. None of them implies another.
/// </summary>
public sealed record MobileStockTakeLifecycleDto(
    long Id,
    string Reference,
    MobileStockTakeScopeDto Scope,
    string ErpStatus,
    string Stage,
    long Revision,
    DateTime CreatedAtUtc,
    DateTime? SubmittedAtUtc,
    int SubmissionJobCount,
    MobileStockTakeSubmissionDto? LatestSubmission,
    MobileStockTakeReconciliationDto Reconciliation,
    MobileStockTakeApprovalDto Approval,
    MobileStockTakeInventoryAdjustmentDto InventoryAdjustment,
    MobileStockTakeResultsStatusDto Results);

public sealed record MobileStockTakeSubmissionPageDto(
    IReadOnlyList<MobileStockTakeSubmissionDto> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record MobileStockTakeResultLineDto(
    long Id,
    long ProductId,
    long VariantId,
    string ProductName,
    string ProductCode,
    string VariantName,
    decimal CountedQuantity,
    decimal BaselineQuantity,
    decimal Difference);

public sealed record MobileStockTakeResultLinePageDto(
    IReadOnlyList<MobileStockTakeResultLineDto> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record MobileStockTakeResultsDto(
    long StockTakeId,
    string ErpStatus,
    string Availability,
    IReadOnlyList<string> Reasons,
    DateTime? ApprovedAtUtc,
    int? CountedLineCount,
    int? DifferenceLineCount,
    decimal? NetDifference,
    MobileStockTakeResultLinePageDto? Lines);

