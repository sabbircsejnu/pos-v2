using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.ReadModels;

namespace RetailPOS.Application.Inventory.Queries;

public sealed record GetAllInventoryQuery : IQuery<IReadOnlyList<InventoryReadModel>>;

public sealed class GetAllInventoryQueryHandler(IInventoryReadStore store)
    : IRequestHandler<GetAllInventoryQuery, IReadOnlyList<InventoryReadModel>>
{
    public Task<IReadOnlyList<InventoryReadModel>> Handle(
        GetAllInventoryQuery request,
        CancellationToken cancellationToken) =>
        store.GetAllAsync(cancellationToken);
}
