namespace RetailPOS.Application.FinancialPeriods.ReadModels;

public sealed record FinancialYearReadModel(
    long Id,
    string YearCode,
    string Name,
    DateTime StartDate,
    DateTime EndDate,
    bool IsCurrent,
    bool IsClosed,
    DateTime? ClosedAt,
    long? ClosedBy);

public sealed record AccountingPeriodReadModel(
    long Id,
    long FinancialYearId,
    int PeriodNumber,
    string Name,
    DateTime StartDate,
    DateTime EndDate,
    string Status,
    string? StatusReason,
    DateTime? ClosedAt,
    long? ClosedBy,
    DateTime? LockedAt,
    long? LockedBy,
    DateTime? ReopenedAt,
    long? ReopenedBy);
