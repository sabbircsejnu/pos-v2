using MediatR;
using RetailPOS.Application.Customers.Abstractions;

namespace RetailPOS.Application.Customers.Queries;

public sealed record SearchCustomersQuery(
    string? Query,
    int PageNumber,
    int PageSize) : IRequest<CustomerListReadModel>;

public sealed class SearchCustomersQueryHandler(ICustomerReadStore readStore)
    : IRequestHandler<SearchCustomersQuery, CustomerListReadModel>
{
    public async Task<CustomerListReadModel> Handle(
        SearchCustomersQuery request,
        CancellationToken cancellationToken)
    {
        var (customers, totalCount) = await readStore.SearchAsync(
            request.Query,
            request.PageNumber,
            request.PageSize,
            cancellationToken);

        return new CustomerListReadModel(
            customers,
            totalCount,
            request.PageNumber,
            request.PageSize);
    }
}

public sealed record QuickSearchCustomersQuery(string Query)
    : IRequest<IReadOnlyList<CustomerReadModel>>;

public sealed class QuickSearchCustomersQueryHandler(ICustomerReadStore readStore)
    : IRequestHandler<QuickSearchCustomersQuery, IReadOnlyList<CustomerReadModel>>
{
    public async Task<IReadOnlyList<CustomerReadModel>> Handle(
        QuickSearchCustomersQuery request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
            return [];

        return await readStore.QuickSearchAsync(
            request.Query,
            10,
            cancellationToken);
    }
}

public sealed record GetCustomerByIdQuery(long Id) : IRequest<CustomerReadModel>;

public sealed class GetCustomerByIdQueryHandler(ICustomerReadStore readStore)
    : IRequestHandler<GetCustomerByIdQuery, CustomerReadModel>
{
    public async Task<CustomerReadModel> Handle(
        GetCustomerByIdQuery request,
        CancellationToken cancellationToken) =>
        await readStore.GetByIdAsync(request.Id, cancellationToken)
        ?? throw new KeyNotFoundException($"Customer with ID {request.Id} not found");
}
