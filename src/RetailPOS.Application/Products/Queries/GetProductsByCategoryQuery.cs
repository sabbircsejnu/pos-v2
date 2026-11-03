using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Products.Abstractions;
using RetailPOS.Application.Products.ReadModels;

namespace RetailPOS.Application.Products.Queries;

public sealed record GetProductsByCategoryQuery(long CategoryId) : IQuery<IReadOnlyList<ProductReadModel>>;

public sealed class GetProductsByCategoryQueryHandler(IProductReadStore store)
    : IRequestHandler<GetProductsByCategoryQuery, IReadOnlyList<ProductReadModel>>
{
    public Task<IReadOnlyList<ProductReadModel>> Handle(
        GetProductsByCategoryQuery request,
        CancellationToken cancellationToken) =>
        store.ListByCategoryAsync(request.CategoryId, cancellationToken);
}
