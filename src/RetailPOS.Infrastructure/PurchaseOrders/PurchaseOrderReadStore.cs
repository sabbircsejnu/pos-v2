using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.PurchaseOrders.Abstractions;
using RetailPOS.Application.PurchaseOrders.ReadModels;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.PurchaseOrders;

public sealed class PurchaseOrderReadStore(RetailPOSDbContext dbContext)
    : IPurchaseOrderReadStore
{
    public async Task<decimal> SumTotalAmountAsync(
        string? status,
        DateTime? startDate,
        DateTime? endDate,
        long? businessId,
        CancellationToken cancellationToken)
    {
        var query = dbContext.PurchaseOrders.AsNoTracking().AsQueryable();

        if (businessId.HasValue)
            query = query.WhereBusiness(businessId.Value);

        if (!string.IsNullOrWhiteSpace(status))
        {
            var normalizedStatus = status.ToLower();
            query = query.Where(order => order.Status.ToLower() == normalizedStatus);
        }

        if (startDate.HasValue)
            query = query.Where(order => order.OrderDate >= startDate.Value);

        if (endDate.HasValue)
            query = query.Where(order => order.OrderDate <= endDate.Value);

        return await query.SumAsync(order => order.TotalAmount, cancellationToken);
    }

    public async Task<IReadOnlyList<PurchaseOrderReadModel>> FindPendingApprovalsAsync(
        long? businessId,
        CancellationToken cancellationToken)
    {
        var query = dbContext.PurchaseOrders
            .AsNoTracking()
            .Include(order => order.Supplier)
            .Include(order => order.Warehouse)
            .Include(order => order.Outlet)
            .Include(order => order.Creator)
            .Include(order => order.Items)
            .Where(order => order.Status.ToLower() == "pending")
            .AsQueryable();

        if (businessId.HasValue)
            query = query.WhereBusiness(businessId.Value);

        var orders = await query
            .OrderBy(order => order.OrderDate)
            .ToListAsync(cancellationToken);
        return orders.Select(Map).ToArray();
    }

    public async Task<PurchaseOrderSearchReadModel> SearchAsync(
        string? status,
        long? supplierId,
        string? locationType,
        long? locationId,
        DateTime? startDate,
        DateTime? endDate,
        int pageNumber,
        int pageSize,
        string sortBy,
        string sortOrder,
        long? businessId,
        CancellationToken cancellationToken)
    {
        var query = dbContext.PurchaseOrders
            .AsNoTracking()
            .Include(order => order.Supplier)
            .Include(order => order.Warehouse)
            .Include(order => order.Outlet)
            .Include(order => order.Creator)
            .Include(order => order.Items)
            .AsQueryable();

        if (businessId.HasValue)
            query = query.WhereBusiness(businessId.Value);

        if (!string.IsNullOrWhiteSpace(status))
        {
            var normalizedStatus = status.ToLower();
            query = query.Where(order => order.Status.ToLower() == normalizedStatus);
        }

        if (supplierId.HasValue)
            query = query.Where(order => order.SupplierId == supplierId.Value);

        query = query.WhereDestination(locationType, locationId);

        if (startDate.HasValue)
            query = query.Where(order => order.OrderDate >= startDate.Value);

        if (endDate.HasValue)
            query = query.Where(order => order.OrderDate <= endDate.Value);

        var totalCount = await query.CountAsync(cancellationToken);
        query = sortBy.ToLower() switch
        {
            "total_amount" => sortOrder.ToLower() == "asc"
                ? query.OrderBy(order => order.TotalAmount)
                : query.OrderByDescending(order => order.TotalAmount),
            "supplier" => sortOrder.ToLower() == "asc"
                ? query.OrderBy(order => order.Supplier.Name)
                : query.OrderByDescending(order => order.Supplier.Name),
            "status" => sortOrder.ToLower() == "asc"
                ? query.OrderBy(order => order.Status)
                : query.OrderByDescending(order => order.Status),
            _ => sortOrder.ToLower() == "asc"
                ? query.OrderBy(order => order.OrderDate)
                : query.OrderByDescending(order => order.OrderDate)
        };

        var orders = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PurchaseOrderSearchReadModel(
            orders.Select(Map).ToArray(),
            totalCount,
            pageNumber,
            pageSize);
    }

    public async Task<IReadOnlyList<PurchaseOrderReadModel>> FindAllAsync(
        string? status,
        long? supplierId,
        string? locationType,
        long? locationId,
        long? businessId,
        CancellationToken cancellationToken)
    {
        var query = dbContext.PurchaseOrders
            .AsNoTracking()
            .Include(order => order.Supplier)
            .Include(order => order.Warehouse)
            .Include(order => order.Outlet)
            .Include(order => order.Creator)
            .Include(order => order.Items)
            .AsQueryable();

        if (businessId.HasValue)
            query = query.WhereBusiness(businessId.Value);

        if (!string.IsNullOrWhiteSpace(status))
        {
            var normalizedStatus = status.ToLower();
            query = query.Where(order => order.Status.ToLower() == normalizedStatus);
        }

        if (supplierId.HasValue)
            query = query.Where(order => order.SupplierId == supplierId.Value);

        query = query.WhereDestination(locationType, locationId);

        var orders = await query
            .OrderByDescending(order => order.OrderDate)
            .ToListAsync(cancellationToken);

        return orders.Select(Map).ToArray();
    }

    public async Task<PurchaseOrderReadModel?> FindByIdAsync(
        long purchaseOrderId,
        long? businessId,
        CancellationToken cancellationToken)
    {
        var query = dbContext.PurchaseOrders
            .AsNoTracking()
            .Include(order => order.Supplier)
            .Include(order => order.Warehouse)
            .Include(order => order.Outlet)
            .Include(order => order.Creator)
            .Include(order => order.Grns)
            .Include(order => order.Items)
                .ThenInclude(item => item.Variant)
                    .ThenInclude(variant => variant.Product)
            .AsQueryable();

        if (businessId.HasValue)
            query = query.WhereBusiness(businessId.Value);

        var order = await query.FirstOrDefaultAsync(
            candidate => candidate.Id == purchaseOrderId,
            cancellationToken);
        return order is null ? null : Map(order);
    }

    private static PurchaseOrderReadModel Map(PurchaseOrder order) =>
        new(
            order.Id,
            order.PoNumber,
            order.SupplierId,
            order.Supplier?.Name ?? string.Empty,
            order.WarehouseId,
            order.Warehouse?.Name ?? string.Empty,
            order.OrderDate,
            order.ExpectedDelivery,
            order.TotalAmount,
            order.Status,
            order.Notes,
            order.RejectionReason,
            order.CreatedBy,
            order.Creator?.Name,
            order.CreatedAt,
            order.UpdatedAt,
            order.Grns.OrderByDescending(grn => grn.Id).Select(grn => (long?)grn.Id).FirstOrDefault(),
            order.Items?.Select(item => new PurchaseOrderItemReadModel(
                item.Id,
                item.VariantId,
                item.Variant?.Product?.Name ?? string.Empty,
                item.Variant?.Name ?? string.Empty,
                item.Variant?.Product?.ProductCode,
                item.Variant?.Sku,
                item.Variant?.Attributes,
                item.Quantity,
                item.Unit,
                item.UnitPrice,
                item.Discount,
                item.Tax,
                item.UomUnitCode,
                item.UomLabel,
                item.UomEnteredQuantity,
                item.UomEnteredUnitPrice,
                item.UomOptionId)).ToArray() ?? [],
            order.OutletId,
            order.Outlet?.Name);
}
