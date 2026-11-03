using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;
using RetailPOS.Application.Warehouses;
using RetailPOS.Application.Warehouses.Abstractions;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Warehouses;

public sealed class WarehouseReadStore(RetailPOSDbContext dbContext)
    : IWarehouseReadStore
{
    public async Task<IReadOnlyList<WarehouseReadModel>> GetAllAsync(
        long? businessId,
        string? searchTerm,
        CancellationToken cancellationToken)
    {
        var query = GetWarehousesQuery(businessId);
        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            query = query.Where(warehouse =>
                warehouse.Name.Contains(searchTerm)
                || warehouse.Address.Contains(searchTerm));
        }

        return await ProjectWarehouseReadModels(
                query.OrderBy(warehouse => warehouse.Name),
                businessId)
            .ToListAsync(cancellationToken);
    }

    public Task<WarehouseReadModel?> GetByIdAsync(
        long warehouseId,
        long? businessId,
        CancellationToken cancellationToken) =>
        ProjectWarehouseReadModels(
                GetWarehousesQuery(businessId).Where(warehouse => warehouse.Id == warehouseId),
                businessId)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<WarehouseReadModel>> GetByBusinessIdAsync(
        long businessId,
        CancellationToken cancellationToken) =>
        await ProjectWarehouseReadModels(
                GetWarehousesQuery(businessId).OrderBy(warehouse => warehouse.Name),
                businessId)
            .ToListAsync(cancellationToken);

    public async Task<WarehouseStatsReadModel?> GetStatsAsync(
        long warehouseId,
        long? businessId,
        CancellationToken cancellationToken)
    {
        var warehouse = await GetByIdAsync(warehouseId, businessId, cancellationToken);
        return warehouse is null
            ? null
            : new WarehouseStatsReadModel(
                warehouse.Id,
                warehouse.Name,
                warehouse.Capacity,
                warehouse.PurchaseOrderCount,
                CurrentStock: 0,
                CapacityUtilization: 0);
    }

    private IQueryable<Warehouse> GetWarehousesQuery(long? businessId) =>
        dbContext.Warehouses
            .AsNoTracking()
            .Where(warehouse => !businessId.HasValue || warehouse.BusinessId == businessId.Value);

    private IQueryable<WarehouseReadModel> ProjectWarehouseReadModels(
        IQueryable<Warehouse> warehouses,
        long? businessId) =>
        warehouses.Select(warehouse => new WarehouseReadModel(
                warehouse.Id,
                warehouse.Name,
                warehouse.Address,
                warehouse.Capacity,
                warehouse.ManagerId,
                warehouse.Manager == null ? null : warehouse.Manager.Name,
                dbContext.PurchaseOrders.Count(order =>
                    order.WarehouseId == warehouse.Id
                    && (!businessId.HasValue || order.Warehouse.BusinessId == businessId.Value)),
                warehouse.CreatedAt,
                warehouse.UpdatedAt));
}
