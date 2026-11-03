using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Products.Abstractions;
using RetailPOS.Application.Products.ReadModels;

namespace RetailPOS.Application.Products.Queries;

public sealed record GetProductVariationsQuery(long ProductId)
    : IQuery<IReadOnlyList<ProductVariationReadModel>>;

public sealed class GetProductVariationsQueryHandler(
    IProductVariationsReadStore store)
    : IRequestHandler<GetProductVariationsQuery, IReadOnlyList<ProductVariationReadModel>>
{
    public async Task<IReadOnlyList<ProductVariationReadModel>> Handle(
        GetProductVariationsQuery request,
        CancellationToken cancellationToken)
    {
        var product = await store.FindByProductIdAsync(request.ProductId, cancellationToken);
        if (product is null)
        {
            throw new KeyNotFoundException($"Product with ID {request.ProductId} not found");
        }

        return product.Variations;
    }
}
