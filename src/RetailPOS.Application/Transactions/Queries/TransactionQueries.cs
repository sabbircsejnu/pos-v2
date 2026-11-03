using MediatR;
using RetailPOS.Application.Transactions.Abstractions;

namespace RetailPOS.Application.Transactions.Queries;

public sealed record SearchTransactionsQuery(
    long? AccountId,
    DateTime? StartDate,
    DateTime? EndDate,
    string? Type,
    int PageNumber,
    int PageSize) : IRequest<TransactionListReadModel>;

public sealed class SearchTransactionsQueryHandler(
    ITransactionReadStore readStore,
    ITransactionReadScope scope)
    : IRequestHandler<SearchTransactionsQuery, TransactionListReadModel>
{
    public async Task<TransactionListReadModel> Handle(
        SearchTransactionsQuery request,
        CancellationToken cancellationToken)
    {
        var (transactions, totalCount) = await readStore.SearchAsync(
            scope.BusinessId,
            request.AccountId,
            request.StartDate,
            request.EndDate,
            request.Type,
            request.PageNumber,
            request.PageSize,
            cancellationToken);

        return new TransactionListReadModel(
            transactions,
            totalCount,
            request.PageNumber,
            request.PageSize);
    }
}

public sealed record GetTransactionByIdQuery(long Id)
    : IRequest<TransactionReadModel>;

public sealed class GetTransactionByIdQueryHandler(
    ITransactionReadStore readStore,
    ITransactionReadScope scope)
    : IRequestHandler<GetTransactionByIdQuery, TransactionReadModel>
{
    public async Task<TransactionReadModel> Handle(
        GetTransactionByIdQuery request,
        CancellationToken cancellationToken) =>
        await readStore.GetByIdAsync(
            scope.BusinessId,
            request.Id,
            cancellationToken)
        ?? throw new KeyNotFoundException(
            $"Transaction with ID {request.Id} not found");
}

public sealed record GetAccountTransactionLedgerQuery(long AccountId)
    : IRequest<IReadOnlyList<TransactionReadModel>>;

public sealed class GetAccountTransactionLedgerQueryHandler(
    ITransactionReadStore readStore,
    ITransactionReadScope scope)
    : IRequestHandler<GetAccountTransactionLedgerQuery, IReadOnlyList<TransactionReadModel>>
{
    public async Task<IReadOnlyList<TransactionReadModel>> Handle(
        GetAccountTransactionLedgerQuery request,
        CancellationToken cancellationToken) =>
        await readStore.GetLedgerAsync(
            scope.BusinessId,
            request.AccountId,
            cancellationToken)
        ?? throw new KeyNotFoundException(
            $"Account with ID {request.AccountId} not found");
}
