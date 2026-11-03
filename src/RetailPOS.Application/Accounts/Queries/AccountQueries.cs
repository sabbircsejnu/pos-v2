using MediatR;
using RetailPOS.Application.Accounts.Abstractions;

namespace RetailPOS.Application.Accounts.Queries;

public sealed record GetAccountsQuery(string? Type)
    : IRequest<AccountListReadModel>;

public sealed class GetAccountsQueryHandler(
    IAccountReadStore readStore,
    IAccountReadScope scope)
    : IRequestHandler<GetAccountsQuery, AccountListReadModel>
{
    public async Task<AccountListReadModel> Handle(
        GetAccountsQuery request,
        CancellationToken cancellationToken)
    {
        var accounts = await readStore.GetAllAsync(
            scope.BusinessId,
            request.Type,
            cancellationToken);
        return new AccountListReadModel(accounts, accounts.Count);
    }
}

public sealed record GetAccountByIdQuery(long Id)
    : IRequest<AccountReadModel>;

public sealed class GetAccountByIdQueryHandler(
    IAccountReadStore readStore,
    IAccountReadScope scope)
    : IRequestHandler<GetAccountByIdQuery, AccountReadModel>
{
    public async Task<AccountReadModel> Handle(
        GetAccountByIdQuery request,
        CancellationToken cancellationToken) =>
        await readStore.GetByIdAsync(
            scope.BusinessId,
            request.Id,
            cancellationToken)
        ?? throw new KeyNotFoundException($"Account with ID {request.Id} not found");
}
