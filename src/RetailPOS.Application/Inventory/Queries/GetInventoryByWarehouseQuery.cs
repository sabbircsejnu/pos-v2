using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.ReadModels;

namespace RetailPOS.Application.Inventory.Queries;

public sealed record GetInventoryByWarehouseQuery(long WarehouseId) : IQuery<IReadOnlyList<InventoryReadModel>>;

public sealed class GetInventoryByWarehouseQueryHandler(IInventoryReadStore store)
    : IRequestHandler<GetInventoryByWarehouseQuery, IReadOnlyList<InventoryReadModel>>
{
    public Task<IReadOnlyList<InventoryReadModel>> Handle(
        GetInventoryByWarehouseQuery request,
        CancellationToken cancellationToken) =>
        store.GetByWarehouseAsync(request.WarehouseId, cancellationToken);
}
