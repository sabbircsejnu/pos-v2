using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Application.Sales.ReadModels;

namespace RetailPOS.Application.Sales.Queries;

public sealed record SearchSalesQuery(SaleSearchCriteria Criteria)
    : IQuery<SaleSearchReadModel>;

public sealed class SearchSalesQueryHandler(ISaleReadStore store)
    : IRequestHandler<SearchSalesQuery, SaleSearchReadModel>
{
    public Task<SaleSearchReadModel> Handle(
        SearchSalesQuery request,
        CancellationToken cancellationToken) =>
        store.SearchAsync(request.Criteria, cancellationToken);
}
