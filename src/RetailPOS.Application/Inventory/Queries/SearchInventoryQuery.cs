using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.ReadModels;

namespace RetailPOS.Application.Inventory.Queries;

public sealed record SearchInventoryQuery(InventorySearchCriteria Criteria)
    : IQuery<IReadOnlyList<InventoryReadModel>>;

public sealed class SearchInventoryQueryHandler(IInventoryReadStore store)
    : IRequestHandler<SearchInventoryQuery, IReadOnlyList<InventoryReadModel>>
{
    public Task<IReadOnlyList<InventoryReadModel>> Handle(
        SearchInventoryQuery request,
        CancellationToken cancellationToken) =>
        store.SearchAsync(request.Criteria, cancellationToken);
}
