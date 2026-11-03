using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Products.Abstractions;
using RetailPOS.Application.Products.ReadModels;

namespace RetailPOS.Application.Products.Queries;

public sealed record GetProductByIdQuery(long ProductId, long? OutletId = null, long? WarehouseId = null) : IQuery<ProductReadModel?>;

public sealed class GetProductByIdQueryHandler(IProductReadStore store, IProductStockReadStore stockStore)
    : IRequestHandler<GetProductByIdQuery, ProductReadModel?>
{
    public async Task<ProductReadModel?> Handle(
        GetProductByIdQuery request,
        CancellationToken cancellationToken)
    {
        var product = await store.FindByIdAsync(request.ProductId, cancellationToken);
        if (product is null)
            return null;

        var products = await stockStore.EnrichAsync(
            [product], request.OutletId, request.WarehouseId, cancellationToken);
        return products[0];
    }
}
