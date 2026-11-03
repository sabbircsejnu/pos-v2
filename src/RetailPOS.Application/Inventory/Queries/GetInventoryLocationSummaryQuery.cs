using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.ReadModels;

namespace RetailPOS.Application.Inventory.Queries;

public sealed record GetInventoryLocationSummaryQuery : IQuery<IReadOnlyList<InventoryLocationSummaryReadModel>>;

public sealed class GetInventoryLocationSummaryQueryHandler(IInventoryReadStore store)
    : IRequestHandler<GetInventoryLocationSummaryQuery, IReadOnlyList<InventoryLocationSummaryReadModel>>
{
    public Task<IReadOnlyList<InventoryLocationSummaryReadModel>> Handle(
        GetInventoryLocationSummaryQuery request,
        CancellationToken cancellationToken) =>
        store.GetSummaryByLocationAsync(cancellationToken);
}
