using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.ReadModels;

namespace RetailPOS.Application.Inventory.Queries;

public sealed record GetExpiringInventoryQuery(int Days, InventoryLocationScope LocationScope)
    : IQuery<IReadOnlyList<InventoryReadModel>>;

public sealed class GetExpiringInventoryQueryHandler(IInventoryReadStore store)
    : IRequestHandler<GetExpiringInventoryQuery, IReadOnlyList<InventoryReadModel>>
{
    public Task<IReadOnlyList<InventoryReadModel>> Handle(
        GetExpiringInventoryQuery request,
        CancellationToken cancellationToken) =>
        store.GetExpiringSoonAsync(
            request.Days,
            request.LocationScope,
            cancellationToken);
}
