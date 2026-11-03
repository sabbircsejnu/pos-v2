using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.PurchaseOrders.Abstractions;
using RetailPOS.Application.PurchaseOrders.ReadModels;

namespace RetailPOS.Application.PurchaseOrders.Queries;

public sealed record GetPendingPurchaseOrderApprovalsQuery
    : IQuery<IReadOnlyList<PurchaseOrderReadModel>>;

public sealed class GetPendingPurchaseOrderApprovalsQueryHandler(
    IPurchaseOrderReadStore store,
    IPurchaseOrderTenantScope tenantScope)
    : IRequestHandler<GetPendingPurchaseOrderApprovalsQuery, IReadOnlyList<PurchaseOrderReadModel>>
{
    public async Task<IReadOnlyList<PurchaseOrderReadModel>> Handle(
        GetPendingPurchaseOrderApprovalsQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await store.FindPendingApprovalsAsync(businessId, cancellationToken);
    }
}
