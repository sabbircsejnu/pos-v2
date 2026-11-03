using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.ReadModels;

namespace RetailPOS.Application.Inventory.Queries;

public sealed record GetInventoryByIdQuery(long Id) : IQuery<InventoryReadModel?>;

public sealed class GetInventoryByIdQueryHandler(IInventoryReadStore store)
    : IRequestHandler<GetInventoryByIdQuery, InventoryReadModel?>
{
    public Task<InventoryReadModel?> Handle(
        GetInventoryByIdQuery request,
        CancellationToken cancellationToken) =>
        store.GetByIdAsync(request.Id, cancellationToken);
}
