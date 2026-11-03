namespace RetailPOS.Application.FinancialYearCloseRuns.ReadModels;

public sealed record AccountingFinancialYearCloseRunReadModel(
    long Id,
    long FinancialYearId,
    long FinalAccountingPeriodId,
    long FinalPeriodCloseRunId,
    int RunNumber,
    string Status,
    long PreparedBy,
    DateTime PreparedAt,
    long? ApprovedBy,
    DateTime? ApprovedAt,
    long? ExecutedBy,
    DateTime? ExecutedAt,
    long? YearEndJournalEntryId,
    string? YearEndJournalNumber,
    long? ReversalJournalEntryId,
    long? ReopenedBy,
    DateTime? ReopenedAt,
    string? ReopenReason);
