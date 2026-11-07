using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Inventory.Abstractions;

namespace RetailPOS.Application.Inventory.Queries;

public sealed record GetTotalStockByVariantQuery(long VariantId) : IQuery<decimal>;

public sealed class GetTotalStockByVariantQueryHandler(IInventoryReadStore store)
    : IRequestHandler<GetTotalStockByVariantQuery, decimal>
{
    public Task<decimal> Handle(
        GetTotalStockByVariantQuery request,
        CancellationToken cancellationToken) =>
        store.GetTotalQuantityByVariantAsync(request.VariantId, cancellationToken);
}
