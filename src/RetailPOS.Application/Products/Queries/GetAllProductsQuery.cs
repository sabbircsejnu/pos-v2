using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Products.Abstractions;
using RetailPOS.Application.Products.ReadModels;

namespace RetailPOS.Application.Products.Queries;

public sealed record GetAllProductsQuery : IQuery<IReadOnlyList<ProductReadModel>>;

public sealed class GetAllProductsQueryHandler(IProductReadStore store)
    : IRequestHandler<GetAllProductsQuery, IReadOnlyList<ProductReadModel>>
{
    public Task<IReadOnlyList<ProductReadModel>> Handle(
        GetAllProductsQuery request,
        CancellationToken cancellationToken) =>
        store.ListAllAsync(cancellationToken);
}
