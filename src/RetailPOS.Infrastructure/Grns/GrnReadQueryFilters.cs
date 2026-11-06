using RetailPOS.Application.Grns.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.PurchaseOrders;

namespace RetailPOS.Infrastructure.Grns;

internal static class GrnReadQueryFilters
{
    public static IQueryable<Grn> WhereReadScope(this IQueryable<Grn> query, GrnReadScope scope)
    {
        var scoped = query.WhereBusiness(scope.BusinessId);
        if (scope.HasAllLocations)
            return scoped;

        // Lists, not arrays: array.Contains does not translate in EF Core 9 under C# 14.
        var outletIds = scope.OutletIds.ToList();
        var warehouseIds = scope.WarehouseIds.ToList();
        return scoped.Where(grn =>
            (grn.PurchaseOrder.OutletId != null && outletIds.Contains(grn.PurchaseOrder.OutletId.Value))
            || (grn.PurchaseOrder.WarehouseId != null && warehouseIds.Contains(grn.PurchaseOrder.WarehouseId.Value)));
    }

    public static IQueryable<PurchaseOrder> WhereReadScope(this IQueryable<PurchaseOrder> query, GrnReadScope scope)
    {
        var scoped = query.WhereBusiness(scope.BusinessId);
        if (scope.HasAllLocations)
            return scoped;

        var outletIds = scope.OutletIds.ToList();
        var warehouseIds = scope.WarehouseIds.ToList();
        return scoped.Where(order =>
            (order.OutletId != null && outletIds.Contains(order.OutletId.Value))
            || (order.WarehouseId != null && warehouseIds.Contains(order.WarehouseId.Value)));
    }

    public static IQueryable<Grn> WhereDestination(
        this IQueryable<Grn> query,
        string? locationType,
        long? locationId)
    {
        if (locationType is null)
            return query;

        if (locationType == PurchaseLocationTypes.Outlet)
        {
            return locationId.HasValue
                ? query.Where(grn => grn.PurchaseOrder.OutletId == locationId)
                : query.Where(grn => grn.PurchaseOrder.OutletId != null);
        }

        return locationId.HasValue
            ? query.Where(grn => grn.PurchaseOrder.WarehouseId == locationId)
            : query.Where(grn => grn.PurchaseOrder.WarehouseId != null);
    }

    public static IQueryable<Grn> WhereReceivedDate(this IQueryable<Grn> query, GrnReceivedWindow? window)
    {
        if (window?.FromUtc is DateTime from)
            query = query.Where(grn => grn.ReceivedDate >= from);

        if (window?.ToExclusiveUtc is DateTime to)
            query = query.Where(grn => grn.ReceivedDate < to);

        return query;
    }
}
