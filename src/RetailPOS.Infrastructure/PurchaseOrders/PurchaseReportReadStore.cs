using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.PurchaseOrders.Abstractions;
using RetailPOS.Application.PurchaseOrders.ReadModels;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.PurchaseOrders;

public sealed class PurchaseReportReadStore(RetailPOSDbContext dbContext)
    : IPurchaseReportReadStore
{
    public async Task<PurchaseSummaryReadModel> GetSummaryAsync(
        DateTime startDate,
        DateTime endDate,
        long? businessId,
        CancellationToken cancellationToken)
    {
        var query = ApplyScopeAndDateRange(startDate, endDate, businessId);
        var rows = await query
            .Select(order => new { order.TotalAmount, order.Status })
            .ToListAsync(cancellationToken);
        return new PurchaseSummaryReadModel(
            rows.Count,
            rows.Sum(order => order.TotalAmount),
            rows.Count(order => order.Status == "pending"),
            rows.Count(order => order.Status == "received"));
    }

    public async Task<IReadOnlyList<PurchaseBySupplierReadModel>> GetBySupplierAsync(
        DateTime startDate,
        DateTime endDate,
        long? businessId,
        CancellationToken cancellationToken)
    {
        var grouped = await ApplyScopeAndDateRange(startDate, endDate, businessId)
            .GroupBy(order => order.SupplierId)
            .Select(group => new
            {
                SupplierId = group.Key,
                OrderCount = group.Count(),
                TotalAmount = group.Sum(order => order.TotalAmount)
            })
            .ToListAsync(cancellationToken);
        var supplierIds = grouped.Select(group => group.SupplierId).ToList();
        var supplierNames = await dbContext.Suppliers
            .AsNoTracking()
            .Where(supplier => supplierIds.Contains(supplier.Id)
                && (!businessId.HasValue || supplier.BusinessId == businessId.Value))
            .ToDictionaryAsync(
                supplier => supplier.Id,
                supplier => supplier.Name,
                cancellationToken);

        return grouped
            .Select(group => new PurchaseBySupplierReadModel(
                group.SupplierId,
                supplierNames.GetValueOrDefault(group.SupplierId, string.Empty),
                group.OrderCount,
                group.TotalAmount))
            .OrderByDescending(row => row.TotalAmount)
            .ToArray();
    }

    private IQueryable<RetailPOS.Core.Entities.PurchaseOrder> ApplyScopeAndDateRange(
        DateTime startDate,
        DateTime endDate,
        long? businessId)
    {
        var query = dbContext.PurchaseOrders
            .AsNoTracking()
            .Where(order => order.OrderDate >= startDate
                && order.OrderDate <= endDate);
        if (businessId.HasValue)
            query = query.WhereBusiness(businessId.Value);
        return query;
    }
}
