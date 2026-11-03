using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.ReadModels;

namespace RetailPOS.Application.Inventory.Queries;

public sealed record GetVariantAttributeFilterOptionsQuery(InventoryLocationScope LocationScope)
    : IQuery<IReadOnlyList<VariantAttributeFilterReadModel>>;

public sealed class GetVariantAttributeFilterOptionsQueryHandler(IInventoryReadStore store)
    : IRequestHandler<
        GetVariantAttributeFilterOptionsQuery,
        IReadOnlyList<VariantAttributeFilterReadModel>>
{
    public Task<IReadOnlyList<VariantAttributeFilterReadModel>> Handle(
        GetVariantAttributeFilterOptionsQuery request,
        CancellationToken cancellationToken) =>
        store.GetVariantAttributeFilterOptionsAsync(
            request.LocationScope,
            cancellationToken);
}
