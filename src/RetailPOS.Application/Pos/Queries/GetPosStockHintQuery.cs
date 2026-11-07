using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Pos.Abstractions;
using RetailPOS.Application.Pos.ReadModels;

namespace RetailPOS.Application.Pos.Queries;

public sealed record GetPosStockHintQuery(
    long VariantId,
    long OutletId,
    decimal RequestedQty) : IQuery<PosStockHintReadModel>;

public sealed class GetPosStockHintQueryHandler(
    IPosProductLookupStore store,
    IPosProductLookupCache cache)
    : IRequestHandler<GetPosStockHintQuery, PosStockHintReadModel>
{
    public async Task<PosStockHintReadModel> Handle(
        GetPosStockHintQuery request,
        CancellationToken cancellationToken)
    {
        var cachedQuantity = await cache.GetStockAsync(
            request.VariantId,
            request.OutletId,
            cancellationToken);
        if (cachedQuantity.HasValue)
        {
            return CreateReadModel(request, cachedQuantity.Value, fromCache: true);
        }

        var quantity = await store.GetOutletStockAsync(
            request.VariantId,
            request.OutletId,
            cancellationToken);
        await cache.SetStockAsync(
            request.VariantId,
            request.OutletId,
            quantity,
            cancellationToken);

        return CreateReadModel(request, quantity, fromCache: false);
    }

    private static PosStockHintReadModel CreateReadModel(
        GetPosStockHintQuery request,
        decimal quantity,
        bool fromCache) =>
        new(
            request.VariantId,
            request.OutletId,
            quantity,
            quantity >= request.RequestedQty,
            fromCache);
}
