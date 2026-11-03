using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.PurchaseOrders.Abstractions;
using RetailPOS.Application.PurchaseOrders.ReadModels;

namespace RetailPOS.Application.PurchaseOrders.Queries;

public sealed record GetPurchaseOrdersQuery(
    string? Status,
    long? SupplierId,
    long? WarehouseId,
    string? LocationType = null,
    long? LocationId = null) : IQuery<IReadOnlyList<PurchaseOrderReadModel>>;

public sealed class GetPurchaseOrdersQueryHandler(
    IPurchaseOrderReadStore store,
    IPurchaseOrderTenantScope tenantScope)
    : IRequestHandler<GetPurchaseOrdersQuery, IReadOnlyList<PurchaseOrderReadModel>>
{
    public async Task<IReadOnlyList<PurchaseOrderReadModel>> Handle(
        GetPurchaseOrdersQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await store.FindAllAsync(
            request.Status,
            request.SupplierId,
            request.LocationId.HasValue || !string.IsNullOrWhiteSpace(request.LocationType)
                ? request.LocationType
                : request.WarehouseId.HasValue ? "warehouse" : null,
            request.LocationId ?? request.WarehouseId,
            businessId,
            cancellationToken);
    }
}
