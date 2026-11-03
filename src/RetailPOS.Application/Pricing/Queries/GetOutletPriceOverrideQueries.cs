using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Pricing.Abstractions;
using RetailPOS.Application.Pricing.ReadModels;

namespace RetailPOS.Application.Pricing.Queries;

public sealed record GetOutletPriceOverridesQuery(long OutletId)
    : IQuery<OutletPriceOverrideListReadModel>;

public sealed class GetOutletPriceOverridesQueryHandler(
    IOutletPriceOverrideReadStore store)
    : IRequestHandler<GetOutletPriceOverridesQuery, OutletPriceOverrideListReadModel>
{
    public async Task<OutletPriceOverrideListReadModel> Handle(
        GetOutletPriceOverridesQuery request,
        CancellationToken cancellationToken)
    {
        var overrides = await store.GetByOutletAsync(request.OutletId, cancellationToken);
        return new OutletPriceOverrideListReadModel(overrides, overrides.Count);
    }
}

public sealed record GetOutletPriceOverrideQuery(long Id)
    : IQuery<OutletPriceOverrideReadModel>;

public sealed class GetOutletPriceOverrideQueryHandler(
    IOutletPriceOverrideReadStore store)
    : IRequestHandler<GetOutletPriceOverrideQuery, OutletPriceOverrideReadModel>
{
    public async Task<OutletPriceOverrideReadModel> Handle(
        GetOutletPriceOverrideQuery request,
        CancellationToken cancellationToken)
    {
        return await store.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException(
                $"Outlet price override with ID {request.Id} not found");
    }
}
