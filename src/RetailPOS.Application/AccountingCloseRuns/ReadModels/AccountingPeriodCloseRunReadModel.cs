namespace RetailPOS.Application.AccountingCloseRuns.ReadModels;

public sealed record AccountingPeriodCloseRunReadModel(
    long Id,
    long AccountingPeriodId,
    int RunNumber,
    string Status,
    int EvaluationSequence,
    long PreparedBy,
    DateTime PreparedAt,
    long? ApprovedBy,
    DateTime? ApprovedAt,
    long? ExecutedBy,
    DateTime? ExecutedAt,
    string? ExecutionReason,
    long? ReopenedBy,
    DateTime? ReopenedAt,
    string? ReopenReason,
    IReadOnlyList<AccountingCloseGateSnapshotReadModel> Gates);

public sealed record AccountingCloseGateSnapshotReadModel(
    long Id,
    int EvaluationSequence,
    string GateCode,
    string Status,
    decimal? ExpectedAmount,
    decimal? ActualAmount,
    string? Details,
    bool IsHistoricalCutoverLimitation,
    DateTime EvaluatedAt,
    bool HasApprovedException,
    string? ApprovedExceptionReason,
    long? ExceptionApprovedBy,
    DateTime? ExceptionApprovedAt);
