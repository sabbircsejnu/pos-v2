using MediatR;
using RetailPOS.Application.Sales;
using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Application.Sales.ReadModels;

namespace RetailPOS.Application.Sales.Queries;

public sealed record GetHeldSaleByIdQuery(long Id) : IRequest<HeldSaleReadModel>;

public sealed class GetHeldSaleByIdQueryHandler(
    IHeldSaleStore store,
    IHeldSaleOutletAuthorizer outletAuthorizer)
    : IRequestHandler<GetHeldSaleByIdQuery, HeldSaleReadModel>
{
    public async Task<HeldSaleReadModel> Handle(
        GetHeldSaleByIdQuery request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var outletId = await store.GetOutletIdAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Held sale with ID {request.Id} not found");
        await outletAuthorizer.AuthorizeReadAsync(outletId, cancellationToken);

        var heldSale = await store.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Held sale with ID {request.Id} not found");
        return HeldSaleMapper.ToReadModel(heldSale);
    }
}

public sealed record GetHeldSalesByOutletQuery(long OutletId) : IRequest<HeldSaleListReadModel>;

public sealed class GetHeldSalesByOutletQueryHandler(
    IHeldSaleStore store,
    IHeldSaleOutletAuthorizer outletAuthorizer)
    : IRequestHandler<GetHeldSalesByOutletQuery, HeldSaleListReadModel>
{
    public async Task<HeldSaleListReadModel> Handle(
        GetHeldSalesByOutletQuery request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var outletId = await outletAuthorizer.AuthorizeReadAsync(request.OutletId, cancellationToken);
        var heldSales = await store.GetByOutletAsync(outletId, cancellationToken);
        var result = heldSales.Select(HeldSaleMapper.ToReadModel).ToArray();
        return new HeldSaleListReadModel(result, result.Length);
    }
}

public sealed record GetHeldSalesByCashierQuery(long CashierId) : IRequest<HeldSaleListReadModel>;

public sealed class GetHeldSalesByCashierQueryHandler(
    IHeldSaleStore store,
    IHeldSaleOutletAuthorizer outletAuthorizer)
    : IRequestHandler<GetHeldSalesByCashierQuery, HeldSaleListReadModel>
{
    public async Task<HeldSaleListReadModel> Handle(
        GetHeldSalesByCashierQuery request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var outletIds = await outletAuthorizer.GetAuthorizedOutletIdsAsync(cancellationToken);
        var heldSales = await store.GetByCashierAsync(request.CashierId, outletIds, cancellationToken);
        var result = heldSales.Select(HeldSaleMapper.ToReadModel).ToArray();
        return new HeldSaleListReadModel(result, result.Length);
    }
}
