using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Grns.Abstractions;
using RetailPOS.Application.Grns.ReadModels;

namespace RetailPOS.Application.Grns.Queries;

public sealed record GetPendingGrnPurchaseOrdersQuery
    : IQuery<IReadOnlyList<PendingGrnPurchaseOrderReadModel>>;

public sealed class GetPendingGrnPurchaseOrdersQueryHandler(
    IPendingGrnPurchaseOrderReadStore store,
    IGrnTenantScope tenantScope)
    : IRequestHandler<GetPendingGrnPurchaseOrdersQuery, IReadOnlyList<PendingGrnPurchaseOrderReadModel>>
{
    public async Task<IReadOnlyList<PendingGrnPurchaseOrderReadModel>> Handle(
        GetPendingGrnPurchaseOrdersQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await store.FindPendingAsync(businessId, cancellationToken);
    }
}
