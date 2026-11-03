using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.PurchaseOrders.Abstractions;

namespace RetailPOS.Application.PurchaseOrders.Queries;

public sealed record GetPurchaseOrderTotalAmountQuery(
    string? Status,
    DateTime? StartDate,
    DateTime? EndDate) : IQuery<decimal>;

public sealed class GetPurchaseOrderTotalAmountQueryHandler(
    IPurchaseOrderReadStore store,
    IPurchaseOrderTenantScope tenantScope)
    : IRequestHandler<GetPurchaseOrderTotalAmountQuery, decimal>
{
    public async Task<decimal> Handle(
        GetPurchaseOrderTotalAmountQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await store.SumTotalAmountAsync(
            request.Status,
            request.StartDate,
            request.EndDate,
            businessId,
            cancellationToken);
    }
}
