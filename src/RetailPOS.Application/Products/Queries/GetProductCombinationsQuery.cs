using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Products.Abstractions;
using RetailPOS.Application.Products.ReadModels;

namespace RetailPOS.Application.Products.Queries;

public sealed record GetProductCombinationsQuery(long ProductId)
    : IQuery<IReadOnlyList<ProductCombinationModel>>;

public sealed class GetProductCombinationsQueryHandler(IProductCombinationsReadStore readStore)
    : IRequestHandler<GetProductCombinationsQuery, IReadOnlyList<ProductCombinationModel>>
{
    public async Task<IReadOnlyList<ProductCombinationModel>> Handle(
        GetProductCombinationsQuery request,
        CancellationToken cancellationToken) =>
        await readStore.GetByProductIdAsync(request.ProductId, cancellationToken)
        ?? throw new KeyNotFoundException($"Product with ID {request.ProductId} not found");
}
