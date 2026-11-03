using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Warehouses;
using RetailPOS.Application.Warehouses.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Application.Integration;
using RetailPOS.Infrastructure.Integration;

namespace RetailPOS.Infrastructure.Warehouses;

public sealed class WarehouseWriteStore(
    RetailPOSDbContext dbContext,
    IOperationalLocationSourceEventWriter sourceEvents)
    : IWarehouseWriteStore
{
    public Task<bool> ExistsByNameAsync(
        string name,
        long? excludeWarehouseId,
        long? businessId,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Warehouses
            .Where(warehouse => warehouse.Name.ToLower() == name.ToLower());
        if (businessId.HasValue)
            query = query.Where(warehouse => warehouse.BusinessId == businessId.Value);
        if (excludeWarehouseId.HasValue)
            query = query.Where(warehouse => warehouse.Id != excludeWarehouseId.Value);
        return query.AnyAsync(cancellationToken);
    }

    public Task<bool> ManagerExistsAsync(
        long managerId,
        long? businessId,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Users.Where(user => user.Id == managerId);
        if (businessId.HasValue)
            query = query.Where(user => user.BusinessId == businessId.Value);
        return query.AnyAsync(cancellationToken);
    }

    public async Task<WarehouseReadModel> CreateAsync(
        string name,
        string address,
        int? capacity,
        long? managerId,
        long? businessId,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var warehouse = new Warehouse
        {
            BusinessId = businessId,
            Name = name,
            Address = address,
            Capacity = capacity,
            ManagerId = managerId,
            CreatedAt = now,
            UpdatedAt = now
        };

        dbContext.Warehouses.Add(warehouse);
        await OperationalLocationSourcePersistence.SaveCreatedAsync(dbContext, sourceEvents, warehouse, cancellationToken);
        var managerName = await GetManagerNameAsync(managerId, cancellationToken);
        return new WarehouseReadModel(
            warehouse.Id,
            warehouse.Name,
            warehouse.Address,
            warehouse.Capacity,
            warehouse.ManagerId,
            managerName,
            PurchaseOrderCount: 0,
            warehouse.CreatedAt,
            warehouse.UpdatedAt);
    }

    public async Task<WarehouseReadModel?> UpdateAsync(
        long warehouseId,
        string name,
        string address,
        int? capacity,
        long? managerId,
        long? businessId,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Warehouses.AsQueryable();
        if (businessId.HasValue)
            query = query.Where(warehouse => warehouse.BusinessId == businessId.Value);

        var warehouse = await query.FirstOrDefaultAsync(
            candidate => candidate.Id == warehouseId,
            cancellationToken);
        if (warehouse is null)
            return null;

        warehouse.Name = name;
        warehouse.Address = address;
        warehouse.Capacity = capacity;
        warehouse.ManagerId = managerId;
        warehouse.UpdatedAt = DateTime.UtcNow;
        await OperationalLocationSourcePersistence.SaveUpdatedAsync(dbContext, sourceEvents, warehouse, cancellationToken);
        var managerName = await GetManagerNameAsync(managerId, cancellationToken);
        var purchaseOrderCount = await GetPurchaseOrderCountAsync(
            warehouseId,
            businessId,
            cancellationToken);
        return new WarehouseReadModel(
            warehouse.Id,
            warehouse.Name,
            warehouse.Address,
            warehouse.Capacity,
            warehouse.ManagerId,
            managerName,
            purchaseOrderCount,
            warehouse.CreatedAt,
            warehouse.UpdatedAt);
    }

    public Task<int> GetPurchaseOrderCountAsync(
        long warehouseId,
        long? businessId,
        CancellationToken cancellationToken)
    {
        var query = dbContext.PurchaseOrders
            .Where(order => order.WarehouseId == warehouseId);
        if (businessId.HasValue)
            query = query.Where(order => order.Warehouse.BusinessId == businessId.Value);
        return query.CountAsync(cancellationToken);
    }

    public async Task<bool> DeleteAsync(
        long warehouseId,
        long? businessId,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Warehouses.AsQueryable();
        if (businessId.HasValue)
            query = query.Where(warehouse => warehouse.BusinessId == businessId.Value);

        var warehouse = await query.FirstOrDefaultAsync(
            candidate => candidate.Id == warehouseId,
            cancellationToken);
        if (warehouse is null)
            return false;

        dbContext.Warehouses.Remove(warehouse);
        await OperationalLocationSourcePersistence.SaveDeletedAsync(dbContext, sourceEvents, warehouse, cancellationToken);
        return true;
    }

    private Task<string?> GetManagerNameAsync(
        long? managerId,
        CancellationToken cancellationToken) =>
        managerId.HasValue
            ? dbContext.Users
                .Where(user => user.Id == managerId.Value)
                .Select(user => user.Name)
                .FirstOrDefaultAsync(cancellationToken)
            : Task.FromResult<string?>(null);
}
