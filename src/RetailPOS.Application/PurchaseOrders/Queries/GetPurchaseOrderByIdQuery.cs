using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.PurchaseOrders.Abstractions;
using RetailPOS.Application.PurchaseOrders.ReadModels;

namespace RetailPOS.Application.PurchaseOrders.Queries;

public sealed record GetPurchaseOrderByIdQuery(long PurchaseOrderId)
    : IQuery<PurchaseOrderReadModel>;

public sealed class GetPurchaseOrderByIdQueryHandler(
    IPurchaseOrderReadStore store,
    IPurchaseOrderTenantScope tenantScope)
    : IRequestHandler<GetPurchaseOrderByIdQuery, PurchaseOrderReadModel>
{
    public async Task<PurchaseOrderReadModel> Handle(
        GetPurchaseOrderByIdQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await store.FindByIdAsync(request.PurchaseOrderId, businessId, cancellationToken)
            ?? throw new KeyNotFoundException($"Purchase order with ID {request.PurchaseOrderId} not found");
    }
}
