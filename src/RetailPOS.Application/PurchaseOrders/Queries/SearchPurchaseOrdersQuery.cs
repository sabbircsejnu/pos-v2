using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.PurchaseOrders.Abstractions;
using RetailPOS.Application.PurchaseOrders.ReadModels;

namespace RetailPOS.Application.PurchaseOrders.Queries;

public sealed record SearchPurchaseOrdersQuery(
    string? Status,
    long? SupplierId,
    long? WarehouseId,
    DateTime? StartDate,
    DateTime? EndDate,
    int PageNumber,
    int PageSize,
    string SortBy,
    string SortOrder,
    string? LocationType = null,
    long? LocationId = null) : IQuery<PurchaseOrderSearchReadModel>;

public sealed class SearchPurchaseOrdersQueryHandler(
    IPurchaseOrderReadStore store,
    IPurchaseOrderTenantScope tenantScope)
    : IRequestHandler<SearchPurchaseOrdersQuery, PurchaseOrderSearchReadModel>
{
    public async Task<PurchaseOrderSearchReadModel> Handle(
        SearchPurchaseOrdersQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await store.SearchAsync(
            request.Status,
            request.SupplierId,
            request.LocationId.HasValue || !string.IsNullOrWhiteSpace(request.LocationType)
                ? request.LocationType
                : request.WarehouseId.HasValue ? "warehouse" : null,
            request.LocationId ?? request.WarehouseId,
            request.StartDate,
            request.EndDate,
            request.PageNumber,
            request.PageSize,
            request.SortBy,
            request.SortOrder,
            businessId,
            cancellationToken);
    }
}
