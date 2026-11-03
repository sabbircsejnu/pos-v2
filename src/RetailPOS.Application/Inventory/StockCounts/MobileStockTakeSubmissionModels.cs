namespace RetailPOS.Application.Inventory.StockCounts;

public sealed record AcceptMobileStockTakeSubmissionInput(
    long StockCountId,
    long BusinessId,
    long OutletId,
    long ActorUserId,
    string ActorRoleName,
    string IdempotencyKey,
    long AcceptedCountRevision,
    string RequestCorrelationId);

public sealed record MobileStockTakeSubmissionReadModel(
    Guid JobId,
    long StockCountId,
    long AcceptedCountRevision,
    string Status,
    int AttemptCount,
    DateTime? NextAttemptAtUtc,
    string? FailureCode,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? CompletedAtUtc,
    string SessionStatus,
    string MovementReconciliationStatus);

public sealed record AcceptMobileStockTakeSubmissionResult(
    string? ErrorCode,
    long CurrentRevision,
    MobileStockTakeSubmissionReadModel? Job,
    bool IsReplay)
{
    public static AcceptMobileStockTakeSubmissionResult Error(
        string errorCode,
        long currentRevision,
        MobileStockTakeSubmissionReadModel? job = null) =>
        new(errorCode, currentRevision, job, false);
}

public static class MobileStockTakeSubmissionErrorCodes
{
    public const string NotFound = "STOCK_TAKE_NOT_FOUND";
    public const string NotDraft = "STOCK_TAKE_NOT_DRAFT";
    public const string StaleRevision = "STALE_SESSION_REVISION";
    public const string NoAcceptedCounts = "NO_ACCEPTED_COUNTS";
    public const string IdempotencyMismatch = "IDEMPOTENCY_KEY_PAYLOAD_MISMATCH";
    public const string InProgress = "STOCK_TAKE_SUBMISSION_IN_PROGRESS";
}

/// <summary>Stable, non-sensitive failure codes persisted on a submission job.</summary>
public static class MobileStockTakeSubmissionFailureCodes
{
    public const string SessionNotFound = "STOCK_TAKE_NOT_FOUND";
    public const string SessionStateChanged = "STOCK_TAKE_STATE_CHANGED";
    public const string RevisionChanged = "COUNT_REVISION_CHANGED";
    public const string ActorNotAuthorized = "ACTOR_NOT_AUTHORIZED";
    public const string ErpSubmissionRejected = "ERP_SUBMISSION_REJECTED";
    public const string TransientFailure = "TRANSIENT_FAILURE";
    public const string AttemptsExhausted = "RETRY_ATTEMPTS_EXHAUSTED";
}

public enum MobileStockTakeSubmissionProcessingOutcome
{
    NotFound,
    AlreadyTerminal,
    NotDue,
    ClaimedElsewhere,
    ErpSubmitted,
    Failed,
    RetryScheduled
}

public sealed record MobileStockTakeSubmissionActorContext(
    long ActorUserId,
    long BusinessId,
    long OutletId);
