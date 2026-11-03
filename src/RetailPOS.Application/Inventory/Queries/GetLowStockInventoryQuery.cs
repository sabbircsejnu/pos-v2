using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.ReadModels.Alerts;

namespace RetailPOS.Application.Inventory.Queries;

public sealed record GetLowStockInventoryQuery(InventoryLocationScope LocationScope)
    : IQuery<IReadOnlyList<LowStockReadModel>>;

public sealed class GetLowStockInventoryQueryHandler(IInventoryReadStore store)
    : IRequestHandler<GetLowStockInventoryQuery, IReadOnlyList<LowStockReadModel>>
{
    public Task<IReadOnlyList<LowStockReadModel>> Handle(
        GetLowStockInventoryQuery request,
        CancellationToken cancellationToken) =>
        store.GetLowStockAsync(request.LocationScope, cancellationToken);
}
