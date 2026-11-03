using MediatR;
using RetailPOS.Application.Products.Abstractions;
using RetailPOS.Application.Products.ReadModels;

namespace RetailPOS.Application.Products.Queries;

public sealed record GetProductImagesQuery(long ProductId) : IRequest<IReadOnlyList<ProductImageReadModel>>;

public sealed class GetProductImagesQueryHandler(IProductMediaStore store)
    : IRequestHandler<GetProductImagesQuery, IReadOnlyList<ProductImageReadModel>>
{
    public Task<IReadOnlyList<ProductImageReadModel>> Handle(
        GetProductImagesQuery request,
        CancellationToken cancellationToken) =>
        store.ListAsync(request.ProductId, cancellationToken);
}
