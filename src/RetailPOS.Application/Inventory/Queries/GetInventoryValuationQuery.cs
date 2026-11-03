using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.ReadModels;

namespace RetailPOS.Application.Inventory.Queries;

public sealed record GetInventoryValuationQuery(InventoryLocationScope LocationScope)
    : IQuery<InventoryValuationReadModel>;

public sealed class GetInventoryValuationQueryHandler(IInventoryReadStore store)
    : IRequestHandler<GetInventoryValuationQuery, InventoryValuationReadModel>
{
    public Task<InventoryValuationReadModel> Handle(
        GetInventoryValuationQuery request,
        CancellationToken cancellationToken) =>
        store.GetValuationAsync(request.LocationScope, cancellationToken);
}
