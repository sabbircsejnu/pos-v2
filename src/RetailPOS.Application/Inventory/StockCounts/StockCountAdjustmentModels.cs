using RetailPOS.Application.Inventory.StockAdjustments;

namespace RetailPOS.Application.Inventory.StockCounts;

public sealed record ApproveStockCountWithAdjustmentInput(
    long StockCountId,
    long BusinessId,
    long LocationId,
    string LocationType,
    long ApproverUserId,
    string ApproverRoleName,
    string RequestCorrelationId);

public sealed record StockCountAdjustmentJobReadModel(
    Guid JobId,
    long StockCountId,
    long ApprovedCountRevision,
    string Status,
    int AttemptCount,
    DateTime? NextAttemptAtUtc,
    string? FailureCode,
    string? EvidenceStatus,
    IReadOnlyList<string> BlockedReasonCodes,
    long? StockAdjustmentId,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? CompletedAtUtc);

public sealed record ApproveStockCountWithAdjustmentResult(
    StockCountAdjustmentJobReadModel Job,
    bool IsReplay);

/// <summary>The approver may not post automatically: a required permission, separation of duty or scope is missing.</summary>
public sealed class StockCountApprovalPolicyException(string message) : InvalidOperationException(message);

/// <summary>Stable, non-sensitive failure codes persisted on an adjustment job.</summary>
public static class StockCountAdjustmentFailureCodes
{
    public const string CountNotFound = "STOCK_COUNT_NOT_FOUND";
    public const string CountStateChanged = "STOCK_COUNT_STATE_CHANGED";
    public const string RevisionChanged = "COUNT_REVISION_CHANGED";
    public const string ApproverNotAuthorized = "APPROVER_NOT_AUTHORIZED";
    public const string MakerCheckerViolation = "MAKER_CHECKER_VIOLATION";
    public const string AdjustmentAlreadyExists = "ADJUSTMENT_ALREADY_EXISTS";
    public const string AdjustmentValidationFailed = "ADJUSTMENT_VALIDATION_FAILED";
    public const string TransientFailure = "TRANSIENT_FAILURE";
    public const string AttemptsExhausted = "RETRY_ATTEMPTS_EXHAUSTED";
}

public enum StockCountAdjustmentProcessingOutcome
{
    NotFound,
    AlreadyTerminal,
    NotDue,
    ClaimedElsewhere,
    Completed,
    Blocked,
    Failed,
    RetryScheduled
}

public sealed record StockCountAdjustmentOptions(
    int MaxAttempts = 5,
    int LeaseSeconds = 300,
    int BaseRetrySeconds = 10,
    int MaxRetrySeconds = 900,
    int PendingRecoveryGraceSeconds = 5,
    int SweepBatchSize = 20,
    int SweepIntervalSeconds = 5)
{
    public void Validate()
    {
        if (MaxAttempts is < 1 or > 20
            || LeaseSeconds < 30
            || BaseRetrySeconds < 1
            || MaxRetrySeconds < BaseRetrySeconds
            || PendingRecoveryGraceSeconds < 0
            || SweepBatchSize is < 1 or > 200
            || SweepIntervalSeconds < 1)
        {
            throw new InvalidOperationException("Stock Count adjustment worker bounds are invalid.");
        }
    }
}

/// <summary>Runtime feature switch for asynchronous StockCount approval/adjustment (read per request).</summary>
public interface IStockCountAsyncAdjustmentSwitch
{
    bool Enabled { get; }
}

public sealed record StockCountAdjustmentApproverAuthorization(
    IReadOnlyList<StockAdjustmentLocationScope> Locations);
