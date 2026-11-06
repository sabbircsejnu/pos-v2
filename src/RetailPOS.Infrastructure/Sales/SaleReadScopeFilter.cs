using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Core.Entities;

namespace RetailPOS.Infrastructure.Sales;

internal static class SaleReadScopeFilter
{
    /// <summary>
    /// Sales of the scope's outlets, in its Business. A sale from before Business tenancy has no Business of its own,
    /// so its outlet decides.
    /// </summary>
    public static IQueryable<Sale> WhereReadScope(this IQueryable<Sale> query, SaleReadScope scope)
    {
        var businessId = scope.BusinessId;
        // A list, not an array: array.Contains does not translate in EF Core 9 under C# 14.
        var outletIds = scope.OutletIds.ToList();
        return query.Where(sale =>
            outletIds.Contains(sale.OutletId)
            && sale.Outlet.BusinessId == businessId
            && (sale.BusinessId == null || sale.BusinessId == businessId));
    }
}
