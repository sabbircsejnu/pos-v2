using RetailPOS.Application.Transactions;

namespace RetailPOS.Application.Transactions.Abstractions;

public interface ITransactionReadStore
{
    Task<(IReadOnlyList<TransactionReadModel> Transactions, int TotalCount)> SearchAsync(
        long? businessId,
        long? accountId,
        DateTime? startDate,
        DateTime? endDate,
        string? type,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken);

    Task<TransactionReadModel?> GetByIdAsync(
        long? businessId,
        long transactionId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<TransactionReadModel>?> GetLedgerAsync(
        long? businessId,
        long accountId,
        CancellationToken cancellationToken);
}

public interface ITransactionReadScope
{
    long? BusinessId { get; }
}
