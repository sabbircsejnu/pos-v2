namespace RetailPOS.Application.BankStatements.ReadModels;

public sealed record BankStatementImportReadModel(
    long Id,
    long BusinessId,
    long BankAccountId,
    string SourceFileName,
    string SourceFileChecksum,
    string SourceFormat,
    DateTime ImportedAt,
    DateTime? StatementPeriodStart,
    DateTime? StatementPeriodEnd,
    decimal? OpeningBalance,
    decimal? ClosingBalance,
    int ImportedRowCount,
    int AcceptedRowCount,
    int RejectedRowCount,
    string Status,
    string? ErrorSummary,
    long? CreatedBy);

public sealed record BankStatementLineReadModel(
    long Id,
    long BusinessId,
    long BankAccountId,
    long BankStatementImportId,
    int SourceRowNumber,
    DateTime TransactionDate,
    DateTime? ValueDate,
    string? ExternalTransactionReference,
    string Description,
    decimal DebitAmount,
    decimal CreditAmount,
    decimal SignedAmount,
    decimal? RunningBalance,
    string? Currency,
    string? RawReferenceText);
