using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Products.Abstractions;
using RetailPOS.Application.Products.ReadModels;

namespace RetailPOS.Application.Products.Queries;

public sealed record SearchProductsQuery(ProductSearchCriteria Criteria) : IQuery<ProductSearchReadModel>;

public sealed class SearchProductsQueryHandler(IProductReadStore store, IProductStockReadStore stockStore)
    : IRequestHandler<SearchProductsQuery, ProductSearchReadModel>
{
    public async Task<ProductSearchReadModel> Handle(
        SearchProductsQuery request,
        CancellationToken cancellationToken)
    {
        var result = await store.SearchAsync(request.Criteria, cancellationToken);
        return result with
        {
            Products = await stockStore.EnrichAsync(
                result.Products, request.Criteria.OutletId, request.Criteria.WarehouseId, cancellationToken)
        };
    }
}
