namespace RetailPOS.Application.Warehouses.Abstractions;

public interface IWarehouseReadStore
{
    Task<IReadOnlyList<WarehouseReadModel>> GetAllAsync(
        long? businessId,
        string? searchTerm,
        CancellationToken cancellationToken);

    Task<WarehouseReadModel?> GetByIdAsync(
        long warehouseId,
        long? businessId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<WarehouseReadModel>> GetByBusinessIdAsync(
        long businessId,
        CancellationToken cancellationToken);

    Task<WarehouseStatsReadModel?> GetStatsAsync(
        long warehouseId,
        long? businessId,
        CancellationToken cancellationToken);
}

public interface IWarehouseReadScopeProvider
{
    Task<long?> GetBusinessIdAsync(CancellationToken cancellationToken);
}

public interface IWarehouseWriteStore
{
    Task<bool> ExistsByNameAsync(
        string name,
        long? excludeWarehouseId,
        long? businessId,
        CancellationToken cancellationToken);

    Task<bool> ManagerExistsAsync(
        long managerId,
        long? businessId,
        CancellationToken cancellationToken);

    Task<WarehouseReadModel> CreateAsync(
        string name,
        string address,
        int? capacity,
        long? managerId,
        long? businessId,
        CancellationToken cancellationToken);

    Task<WarehouseReadModel?> UpdateAsync(
        long warehouseId,
        string name,
        string address,
        int? capacity,
        long? managerId,
        long? businessId,
        CancellationToken cancellationToken);

    Task<int> GetPurchaseOrderCountAsync(
        long warehouseId,
        long? businessId,
        CancellationToken cancellationToken);

    Task<bool> DeleteAsync(
        long warehouseId,
        long? businessId,
        CancellationToken cancellationToken);
}
