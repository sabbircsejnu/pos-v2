using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.ReadModels;

namespace RetailPOS.Application.Inventory.Queries;

public sealed record GetInventoryByOutletQuery(long OutletId) : IQuery<IReadOnlyList<InventoryReadModel>>;

public sealed class GetInventoryByOutletQueryHandler(IInventoryReadStore store)
    : IRequestHandler<GetInventoryByOutletQuery, IReadOnlyList<InventoryReadModel>>
{
    public Task<IReadOnlyList<InventoryReadModel>> Handle(
        GetInventoryByOutletQuery request,
        CancellationToken cancellationToken) =>
        store.GetByOutletAsync(request.OutletId, cancellationToken);
}
