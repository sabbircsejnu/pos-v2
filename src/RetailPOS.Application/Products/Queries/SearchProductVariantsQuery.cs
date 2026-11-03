using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Products.Abstractions;
using RetailPOS.Application.Products.ReadModels;

namespace RetailPOS.Application.Products.Queries;

public sealed record SearchProductVariantsQuery(ProductVariantSearchCriteria Criteria)
    : IQuery<IReadOnlyList<ProductVariantSearchReadModel>>;

public sealed class SearchProductVariantsQueryHandler(IProductReadStore store)
    : IRequestHandler<SearchProductVariantsQuery, IReadOnlyList<ProductVariantSearchReadModel>>
{
    public Task<IReadOnlyList<ProductVariantSearchReadModel>> Handle(
        SearchProductVariantsQuery request,
        CancellationToken cancellationToken) =>
        store.SearchVariantsAsync(request.Criteria, cancellationToken);
}
