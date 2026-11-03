namespace RetailPOS.Application.Transactions;

public sealed record TransactionReadModel(
    long Id,
    long AccountId,
    string AccountName,
    decimal Amount,
    string Type,
    string? Description,
    DateTime TransactionDate,
    string? ReferenceType,
    long? ReferenceId);

public sealed record TransactionListReadModel(
    IReadOnlyList<TransactionReadModel> Transactions,
    int TotalCount,
    int PageNumber,
    int PageSize);
