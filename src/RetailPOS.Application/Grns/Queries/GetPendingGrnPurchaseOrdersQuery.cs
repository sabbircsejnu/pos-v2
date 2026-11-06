using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Grns.Abstractions;
using RetailPOS.Application.Grns.ReadModels;

namespace RetailPOS.Application.Grns.Queries;

public sealed record GetPendingGrnPurchaseOrdersQuery
    : IQuery<IReadOnlyList<PendingGrnPurchaseOrderReadModel>>;

public sealed class GetPendingGrnPurchaseOrdersQueryHandler(
    IPendingGrnPurchaseOrderReadStore store,
    IGrnReadScopeProvider scopeProvider)
    : IRequestHandler<GetPendingGrnPurchaseOrdersQuery, IReadOnlyList<PendingGrnPurchaseOrderReadModel>>
{
    public async Task<IReadOnlyList<PendingGrnPurchaseOrderReadModel>> Handle(
        GetPendingGrnPurchaseOrdersQuery request,
        CancellationToken cancellationToken)
    {
        var scope = await scopeProvider.GetAsync(cancellationToken);
        return await store.FindPendingAsync(scope, cancellationToken);
    }
}
