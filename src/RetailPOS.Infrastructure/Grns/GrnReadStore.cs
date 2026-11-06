using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Grns.Abstractions;
using RetailPOS.Application.Grns.ReadModels;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.PurchaseOrders;

namespace RetailPOS.Infrastructure.Grns;

public sealed class GrnReadStore(RetailPOSDbContext dbContext) :
    IGrnReadStore,
    IPendingGrnPurchaseOrderReadStore
{
    public async Task<GrnReadModel?> FindByIdAsync(
        long grnId,
        GrnReadScope scope,
        CancellationToken cancellationToken)
    {
        var grn = await dbContext.Grns
            .AsNoTracking()
            .Include(grn => grn.PurchaseOrder)
                .ThenInclude(order => order.Supplier)
            .Include(grn => grn.PurchaseOrder)
                .ThenInclude(order => order.Warehouse)
            .Include(grn => grn.PurchaseOrder)
                .ThenInclude(order => order.Outlet)
            .Include(grn => grn.PurchaseOrder)
                .ThenInclude(order => order.Creator)
            .Include(grn => grn.Creator)
            .Include(grn => grn.Items)
                .ThenInclude(item => item.PurchaseOrderItem)
                    .ThenInclude(item => item.Variant)
                        .ThenInclude(variant => variant.Product)
            .WhereReadScope(scope)
            .FirstOrDefaultAsync(
                candidate => candidate.Id == grnId,
                cancellationToken);
        return grn is null ? null : GrnReadModelMapper.Map(grn);
    }

    public async Task<GrnVarianceReadModel?> FindVarianceByIdAsync(
        long grnId,
        GrnReadScope scope,
        CancellationToken cancellationToken)
    {
        var grn = await dbContext.Grns
            .AsNoTracking()
            .Include(grn => grn.PurchaseOrder)
            .Include(grn => grn.Items)
                .ThenInclude(item => item.PurchaseOrderItem)
                    .ThenInclude(item => item.Variant)
                        .ThenInclude(variant => variant.Product)
            .WhereReadScope(scope)
            .FirstOrDefaultAsync(
                candidate => candidate.Id == grnId,
                cancellationToken);
        if (grn is null)
            return null;

        return new GrnVarianceReadModel(
            grn.Id,
            grn.PurchaseOrder.Id,
            grn.Status,
            grn.Items.Select(item => new GrnVarianceItemReadModel(
                item.PoItemId,
                item.PurchaseOrderItem.VariantId,
                item.PurchaseOrderItem.Variant?.Product?.Name ?? string.Empty,
                item.PurchaseOrderItem.Variant?.Name ?? string.Empty,
                item.PurchaseOrderItem.Quantity,
                item.ReceivedQty))
                .ToArray());
    }

    public async Task<IReadOnlyList<GrnReadModel>> FindAllAsync(
        long? purchaseOrderId,
        string? status,
        GrnReadScope scope,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Grns
            .AsNoTracking()
            .Include(grn => grn.PurchaseOrder)
                .ThenInclude(order => order.Supplier)
            .Include(grn => grn.PurchaseOrder)
                .ThenInclude(order => order.Warehouse)
            .Include(grn => grn.PurchaseOrder)
                .ThenInclude(order => order.Outlet)
            .Include(grn => grn.PurchaseOrder)
                .ThenInclude(order => order.Creator)
            .Include(grn => grn.Creator)
            .Include(grn => grn.Items)
            .AsQueryable();

        if (purchaseOrderId.HasValue)
            query = query.Where(grn => grn.PoId == purchaseOrderId.Value);

        if (!string.IsNullOrWhiteSpace(status))
        {
            var normalizedStatus = status.ToLower();
            query = query.Where(grn => grn.Status.ToLower() == normalizedStatus);
        }

        var grns = await query
            .WhereReadScope(scope)
            .OrderByDescending(grn => grn.ReceivedDate)
            .ToListAsync(cancellationToken);
        return grns.Select(GrnReadModelMapper.Map).ToArray();
    }

    public async Task<GrnSearchReadModel> SearchAsync(
        GrnSearchCriteria criteria,
        GrnReadScope scope,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Grns
            .AsNoTracking()
            .Include(grn => grn.PurchaseOrder)
                .ThenInclude(order => order.Supplier)
            .Include(grn => grn.PurchaseOrder)
                .ThenInclude(order => order.Warehouse)
            .Include(grn => grn.PurchaseOrder)
                .ThenInclude(order => order.Outlet)
            .Include(grn => grn.PurchaseOrder)
                .ThenInclude(order => order.Creator)
            .Include(grn => grn.Creator)
            .Include(grn => grn.Items)
            .AsQueryable();

        if (criteria.PurchaseOrderId.HasValue)
            query = query.Where(grn => grn.PoId == criteria.PurchaseOrderId.Value);

        if (!string.IsNullOrWhiteSpace(criteria.Status))
        {
            var normalizedStatus = criteria.Status.ToLower();
            query = query.Where(grn => grn.Status.ToLower() == normalizedStatus);
        }

        if (criteria.StartDate.HasValue)
            query = query.Where(grn => grn.ReceivedDate >= criteria.StartDate.Value);

        if (criteria.EndDate.HasValue)
            query = query.Where(grn => grn.ReceivedDate <= criteria.EndDate.Value);

        query = query
            .WhereReadScope(scope)
            .WhereDestination(criteria.LocationType, criteria.LocationId)
            .WhereReceivedDate(criteria.ReceivedDate);

        var totalCount = await query.CountAsync(cancellationToken);
        var grns = await query
            .OrderByDescending(grn => grn.ReceivedDate)
            .ThenByDescending(grn => grn.Id)
            .Skip((criteria.PageNumber - 1) * criteria.PageSize)
            .Take(criteria.PageSize)
            .ToListAsync(cancellationToken);

        return new GrnSearchReadModel(
            grns.Select(GrnReadModelMapper.Map).ToArray(),
            totalCount,
            criteria.PageNumber,
            criteria.PageSize);
    }

    public async Task<IReadOnlyList<PendingGrnPurchaseOrderReadModel>> FindPendingAsync(
        GrnReadScope scope,
        CancellationToken cancellationToken)
    {
        var orders = await dbContext.PurchaseOrders
            .AsNoTracking()
            .Include(order => order.Supplier)
            .Include(order => order.Warehouse)
            .Include(order => order.Outlet)
            .Include(order => order.Items)
                .ThenInclude(item => item.Variant)
                    .ThenInclude(variant => variant.Product)
                        .ThenInclude(product => product.Images.Where(image => image.IsPrimary))
            .Where(order => order.Status.ToLower() == "approved"
                || order.Status.ToLower() == "partial")
            .WhereReadScope(scope)
            .OrderBy(order => order.OrderDate)
            .ToListAsync(cancellationToken);

        return orders.Select(order => new PendingGrnPurchaseOrderReadModel(
            order.Id,
            order.SupplierId,
            order.Supplier?.Name ?? string.Empty,
            order.WarehouseId,
            order.Warehouse?.Name ?? string.Empty,
            order.OrderDate,
            order.TotalAmount,
            order.Status,
            order.Items.Select(item => new PendingGrnPurchaseOrderItemReadModel(
                item.Id,
                item.VariantId,
                item.Variant?.Product?.Name ?? string.Empty,
                item.Variant?.Product?.ProductCode,
                item.Variant?.Name ?? string.Empty,
                item.Variant?.Attributes,
                item.Quantity,
                item.UnitPrice,
                item.Variant?.Product?.Images.FirstOrDefault(image => image.IsPrimary)?.ThumbPath))
                .ToArray(),
            order.OutletId,
            order.Outlet?.Name))
            .ToArray();
    }
}
