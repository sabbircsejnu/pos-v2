using System.Linq.Expressions;
using RetailPOS.Core.Entities;

namespace RetailPOS.Infrastructure.PurchaseOrders;

/// <summary>
/// Business scoping for purchase orders whose destination is either a Warehouse or an Outlet.
/// The Business is derived from the destination location, so legacy warehouse-only rows keep
/// resolving exactly as before.
/// </summary>
public static class PurchaseOrderBusinessScope
{
    public static Expression<Func<PurchaseOrder, bool>> BelongsTo(long businessId) =>
        order => (order.WarehouseId != null && order.Warehouse!.BusinessId == businessId)
            || (order.OutletId != null && order.Outlet!.BusinessId == businessId);

    public static IQueryable<PurchaseOrder> WhereBusiness(
        this IQueryable<PurchaseOrder> query,
        long businessId) =>
        query.Where(BelongsTo(businessId));

    public static IQueryable<PurchaseOrder> WhereBusiness(
        this IQueryable<PurchaseOrder> query,
        long? businessId) =>
        businessId.HasValue ? query.WhereBusiness(businessId.Value) : query;

    public static IQueryable<Grn> WhereBusiness(
        this IQueryable<Grn> query,
        long businessId) =>
        query.Where(grn =>
            (grn.PurchaseOrder.WarehouseId != null
                && grn.PurchaseOrder.Warehouse!.BusinessId == businessId)
            || (grn.PurchaseOrder.OutletId != null
                && grn.PurchaseOrder.Outlet!.BusinessId == businessId));

    public static IQueryable<Grn> WhereBusiness(
        this IQueryable<Grn> query,
        long? businessId) =>
        businessId.HasValue ? query.WhereBusiness(businessId.Value) : query;

    /// <summary>
    /// Filters by destination. <paramref name="locationType"/> defaults to warehouse when only a
    /// legacy <c>warehouseId</c> filter is supplied.
    /// </summary>
    public static IQueryable<PurchaseOrder> WhereDestination(
        this IQueryable<PurchaseOrder> query,
        string? locationType,
        long? locationId)
    {
        if (!locationId.HasValue)
        {
            if (string.IsNullOrWhiteSpace(locationType))
                return query;

            return PurchaseLocationTypes.Normalize(locationType) == PurchaseLocationTypes.Outlet
                ? query.Where(order => order.OutletId != null)
                : query.Where(order => order.WarehouseId != null);
        }

        var id = locationId.Value;
        return string.IsNullOrWhiteSpace(locationType)
            || PurchaseLocationTypes.Normalize(locationType) == PurchaseLocationTypes.Warehouse
            ? query.Where(order => order.WarehouseId == id)
            : query.Where(order => order.OutletId == id);
    }
}
