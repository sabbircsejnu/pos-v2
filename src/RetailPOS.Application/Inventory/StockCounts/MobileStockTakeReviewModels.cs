namespace RetailPOS.Application.Inventory.StockCounts;

/// <summary>
/// Deterministic movement-evidence outcome. It is advisory evidence only: it never changes the persisted
/// ERP <c>MovementReconciliationStatus</c>, never approves a count and never posts inventory.
/// </summary>
public static class MobileStockTakeReconciliationStatus
{
    public const string NotEvaluated = "NotEvaluated";
    public const string Blocked = "Blocked";
    public const string Unresolved = "Unresolved";
    public const string NoConflictDetected = "NoConflictDetected";
}

public static class MobileStockTakeReconciliationReasonCodes
{
    public const string SessionNotSubmitted = "SESSION_NOT_SUBMITTED";
    public const string SessionRejected = "SESSION_REJECTED";
    public const string BaselineNotVerifiable = "BASELINE_NOT_VERIFIABLE";
    public const string SubmissionTimeMissing = "COUNT_SUBMISSION_TIME_MISSING";
    public const string NoCountedLines = "NO_COUNTED_LINES";
    public const string BaselineLedgerEvidenceMissing = "BASELINE_LEDGER_EVIDENCE_MISSING";
    public const string BaselineLedgerMismatch = "BASELINE_LEDGER_MISMATCH";
    public const string MovementOrderAnomaly = "MOVEMENT_ORDER_ANOMALY";
    public const string MovementBalanceChainBroken = "MOVEMENT_BALANCE_CHAIN_BROKEN";
    public const string InventoryLedgerMismatch = "INVENTORY_LEDGER_MISMATCH";
    public const string MovementDuringCountWindow = "MOVEMENT_DURING_COUNT_WINDOW";
}

public static class MobileStockTakeResultsAvailability
{
    public const string NotAvailable = "NotAvailable";
    public const string Withheld = "Withheld";
    public const string Available = "Available";
}

public static class MobileStockTakeResultsReasonCodes
{
    public const string NotApproved = "STOCK_TAKE_NOT_APPROVED";
    public const string ApprovalEvidenceMissing = "APPROVAL_EVIDENCE_MISSING";
    public const string LinesUncounted = "RESULT_LINES_UNCOUNTED";
    public const string DifferenceInconsistent = "RESULT_DIFFERENCE_INCONSISTENT";
}

public static class MobileStockTakeLifecycleStages
{
    public const string Counting = "Counting";
    public const string SubmissionInProgress = "SubmissionInProgress";
    public const string SubmissionFailed = "SubmissionFailed";
    public const string SubmittedForErpReview = "SubmittedForErpReview";
    public const string RejectedByErp = "RejectedByErp";
    public const string ApprovedByErp = "ApprovedByErp";
    public const string AdjustmentDraftGenerated = "AdjustmentDraftGenerated";
    public const string Completed = "Completed";
    public const string Unknown = "Unknown";
}

public static class MobileStockTakeAdjustmentStates
{
    public const string NotGenerated = "NotGenerated";
}

public sealed record MobileStockTakeReconciliationReason(string Code, int AffectedLineCount);

/// <summary>Row counts only; quantities are never disclosed by the reconciliation evaluation.</summary>
public sealed record MobileStockTakeMovementSummary(
    int SaleCount,
    int ReturnCount,
    int TransferCount,
    int AdjustmentCount,
    int GoodsReceiptCount,
    int OtherCount,
    int PostCountMovementCount)
{
    public static MobileStockTakeMovementSummary Empty { get; } = new(0, 0, 0, 0, 0, 0, 0);
}

public sealed record MobileStockTakeReconciliationEvaluation(
    string Status,
    IReadOnlyList<MobileStockTakeReconciliationReason> Reasons,
    MobileStockTakeMovementSummary Movements,
    int EvaluatedLineCount,
    DateTime EvaluatedAtUtc);

public sealed record MobileStockTakeHistoryFilter(
    long? OutletId,
    string? Status,
    string? SubmissionStatus,
    string? Search,
    DateTime? CreatedFromUtc,
    DateTime? CreatedToUtc);

public sealed record MobileStockTakeLatestSubmissionReadModel(
    Guid JobId,
    string Status,
    bool IsTerminal,
    int AttemptCount,
    string? FailureCode,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? CompletedAtUtc);

public sealed record MobileStockTakeHistoryItemReadModel(
    long Id,
    string Reference,
    long OutletId,
    string OutletName,
    DateTime StockCountDate,
    string ErpStatus,
    string Stage,
    long CountRevision,
    int TotalItems,
    DateTime CreatedAt,
    DateTime? SubmittedAt,
    DateTime? ApprovedAt,
    DateTime? RejectedAt,
    string ErpMovementReconciliationStatus,
    MobileStockTakeLatestSubmissionReadModel? LatestSubmission);

public sealed record MobileStockTakeHistoryPageReadModel(
    IReadOnlyList<MobileStockTakeHistoryItemReadModel> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record MobileStockTakeLifecycleReadModel(
    long Id,
    string Reference,
    long OutletId,
    string OutletName,
    string ErpStatus,
    string Stage,
    long CountRevision,
    DateTime CreatedAt,
    DateTime? SubmittedAt,
    DateTime? ApprovedAt,
    DateTime? RejectedAt,
    bool HasRejectionReason,
    int SubmissionJobCount,
    MobileStockTakeSubmissionReadModel? LatestSubmission,
    string ErpMovementReconciliationStatus,
    MobileStockTakeReconciliationEvaluation Reconciliation,
    string AdjustmentState,
    bool IsInventoryPosted,
    bool AdjustmentDraftBlockedByErpReconciliation,
    string ResultsAvailability,
    IReadOnlyList<string> ResultsReasons,
    StockCountAdjustmentJobReadModel? AdjustmentJob = null);

public sealed record MobileStockTakeSubmissionPageReadModel(
    IReadOnlyList<MobileStockTakeSubmissionReadModel> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record MobileStockTakeResultLineReadModel(
    long Id,
    long ProductId,
    long VariantId,
    string ProductName,
    string ProductCode,
    string VariantName,
    decimal CountedQuantity,
    decimal BaselineQuantity,
    decimal Difference);

public sealed record MobileStockTakeResultsReadModel(
    long Id,
    string ErpStatus,
    string Availability,
    IReadOnlyList<string> Reasons,
    DateTime? ApprovedAt,
    int? CountedLineCount,
    int? DifferenceLineCount,
    decimal? NetDifference,
    MobileStockTakeResultLinePageReadModel? Lines);

public sealed record MobileStockTakeResultLinePageReadModel(
    IReadOnlyList<MobileStockTakeResultLineReadModel> Items,
    int Page,
    int PageSize,
    int TotalCount);
