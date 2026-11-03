using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.ReadModels;

namespace RetailPOS.Application.Inventory.Queries;

public sealed record GetOutOfStockInventoryQuery(InventoryLocationScope LocationScope)
    : IQuery<IReadOnlyList<InventoryReadModel>>;

public sealed class GetOutOfStockInventoryQueryHandler(IInventoryReadStore store)
    : IRequestHandler<GetOutOfStockInventoryQuery, IReadOnlyList<InventoryReadModel>>
{
    public Task<IReadOnlyList<InventoryReadModel>> Handle(
        GetOutOfStockInventoryQuery request,
        CancellationToken cancellationToken) =>
        store.GetOutOfStockAsync(request.LocationScope, cancellationToken);
}
