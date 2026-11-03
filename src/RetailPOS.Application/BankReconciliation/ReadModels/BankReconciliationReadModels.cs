namespace RetailPOS.Application.BankReconciliation.ReadModels;

public sealed record BankReconciliationSessionReadModel(
    long Id,
    long BusinessId,
    long BankAccountId,
    DateTime PeriodStart,
    DateTime PeriodEnd,
    decimal? StatementOpeningBalance,
    decimal? StatementClosingBalance,
    decimal? GlOpeningBalance,
    string Status);

public sealed record ReconciliationCandidateReadModel(
    long? BankStatementLineId,
    long? JournalEntryLineId,
    DateTime Date,
    decimal SignedAmount,
    string Description,
    string? ExternalReference,
    long? JournalEntryId,
    string? JournalNumber,
    string? SourceType,
    string? SourceReference);

public sealed record BankReconciliationCalculationReadModel(
    decimal StatementOpeningBalance,
    decimal StatementMovement,
    decimal StatementClosingBalance,
    decimal GlOpeningBalance,
    decimal GlMovement,
    decimal GlClosingBalance,
    decimal MatchedStatementAmount,
    int MatchedStatementCount,
    decimal MatchedGlAmount,
    int MatchedGlCount,
    decimal UnmatchedStatementAmount,
    int UnmatchedStatementCount,
    decimal UnmatchedGlAmount,
    int UnmatchedGlCount,
    decimal ReconciliationDifference,
    int UnresolvedExceptionCount);

public sealed record BankAccountReadModel(
    long? Id,
    string DisplayName,
    long ChartAccountId,
    long? OutletId,
    bool IsActive);

public sealed record ReconciliationMatchReadModel(
    long Id,
    long SessionId,
    string Status,
    decimal StatementTotal,
    decimal GlTotal,
    IReadOnlyList<long> StatementLineIds,
    IReadOnlyList<long> JournalEntryLineIds);

public sealed record StatementAdjustmentReadModel(
    long Id,
    long BankStatementLineId,
    string AdjustmentType,
    decimal Amount,
    string Status,
    long? JournalEntryId,
    long? ReversalJournalEntryId);

public sealed record StatementExceptionReadModel(
    long Id,
    long BankStatementLineId,
    string Classification,
    string Status,
    string? Reason,
    string? ResolutionNotes,
    long CreatedBy,
    DateTime CreatedAt,
    long? ResolvedBy,
    DateTime? ResolvedAt);
