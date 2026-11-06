using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Grns.Abstractions;
using RetailPOS.Application.Grns.ReadModels;

namespace RetailPOS.Application.Grns.Queries;

public sealed record GetGrnPoVarianceQuery(long PurchaseOrderId) : IQuery<GrnPoVarianceReadModel>;

public sealed class GetGrnPoVarianceQueryHandler(
    IGrnPoVarianceReadStore store,
    IGrnReadScopeProvider scopeProvider)
    : IRequestHandler<GetGrnPoVarianceQuery, GrnPoVarianceReadModel>
{
    public async Task<GrnPoVarianceReadModel> Handle(
        GetGrnPoVarianceQuery request,
        CancellationToken cancellationToken)
    {
        var scope = await scopeProvider.GetAsync(cancellationToken);
        var variance = await store.FindByPurchaseOrderIdAsync(
            request.PurchaseOrderId,
            scope,
            cancellationToken);
        return variance ?? throw new KeyNotFoundException(
            $"Purchase order with ID {request.PurchaseOrderId} not found");
    }
}
