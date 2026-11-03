using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Inventory.Abstractions;

namespace RetailPOS.Application.Inventory.Queries;

public sealed record GetTotalStockByVariantQuery(long VariantId) : IQuery<int>;

public sealed class GetTotalStockByVariantQueryHandler(IInventoryReadStore store)
    : IRequestHandler<GetTotalStockByVariantQuery, int>
{
    public Task<int> Handle(
        GetTotalStockByVariantQuery request,
        CancellationToken cancellationToken) =>
        store.GetTotalQuantityByVariantAsync(request.VariantId, cancellationToken);
}
