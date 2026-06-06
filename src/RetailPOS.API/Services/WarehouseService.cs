using RetailPOS.API.DTOs.Warehouse;
using RetailPOS.Infrastructure.Repositories;
using RetailPOS.Core.Entities;

namespace RetailPOS.API.Services;

public class WarehouseService : IWarehouseService
{
    private readonly IWarehouseRepository _warehouseRepository;
    private readonly IUserRepository _userRepository;
    private readonly ITenantAccessService _tenantAccess;
    private readonly ILogger<WarehouseService> _logger;

    public WarehouseService(
        IWarehouseRepository warehouseRepository,
        IUserRepository userRepository,
        ITenantAccessService tenantAccess,
        ILogger<WarehouseService> logger)
    {
        _warehouseRepository = warehouseRepository;
        _userRepository = userRepository;
        _tenantAccess = tenantAccess;
        _logger = logger;
    }

    public async Task<IEnumerable<WarehouseDto>> GetAllWarehousesAsync()
    {
        long? businessId = _tenantAccess.IsSuperAdmin ? null : _tenantAccess.RequireBusinessId();
        var warehouses = await _warehouseRepository.GetAllAsync(businessId);
        var warehouseDtos = new List<WarehouseDto>();

        foreach (var warehouse in warehouses)
        {
            var poCount = await _warehouseRepository.GetPurchaseOrderCountAsync(warehouse.Id, businessId);
            warehouseDtos.Add(MapToDto(warehouse, poCount));
        }

        return warehouseDtos;
    }

    public async Task<WarehouseDto?> GetWarehouseByIdAsync(long id)
    {
        long? businessId = _tenantAccess.IsSuperAdmin ? null : _tenantAccess.RequireBusinessId();
        var warehouse = await _warehouseRepository.GetByIdAsync(id, businessId);
        if (warehouse == null)
            return null;

        var poCount = await _warehouseRepository.GetPurchaseOrderCountAsync(id, businessId);
        return MapToDto(warehouse, poCount);
    }

    public async Task<WarehouseDto> CreateWarehouseAsync(CreateWarehouseDto dto)
    {
        // Validate warehouse name uniqueness
        long? businessId = _tenantAccess.IsSuperAdmin ? null : _tenantAccess.RequireBusinessId();

        if (await _warehouseRepository.ExistsByNameAsync(dto.Name, null, businessId))
        {
            _logger.LogWarning("Warehouse creation failed: Name '{Name}' already exists", dto.Name);
            throw new InvalidOperationException($"A warehouse with the name '{dto.Name}' already exists");
        }

        // Validate manager exists if provided
        if (dto.ManagerId.HasValue)
        {
            var manager = await _userRepository.GetByIdAsync(dto.ManagerId.Value, businessId);
            if (manager == null)
            {
                _logger.LogWarning("Warehouse creation failed: Manager with ID {ManagerId} not found", dto.ManagerId);
                throw new InvalidOperationException($"Manager with ID {dto.ManagerId} not found");
            }
        }

        // Validate capacity is positive if provided
        if (dto.Capacity.HasValue && dto.Capacity.Value < 0)
        {
            throw new InvalidOperationException("Capacity must be a positive number");
        }

        var warehouse = new Warehouse
        {
            BusinessId = businessId,
            Name = dto.Name,
            Address = dto.Address,
            Capacity = dto.Capacity,
            ManagerId = dto.ManagerId
        };

        var createdWarehouse = await _warehouseRepository.CreateAsync(warehouse);
        _logger.LogInformation("Warehouse created: ID={Id}, Name={Name}", createdWarehouse.Id, createdWarehouse.Name);

        return MapToDto(createdWarehouse, 0);
    }

    public async Task<WarehouseDto> UpdateWarehouseAsync(long id, UpdateWarehouseDto dto)
    {
        long? businessId = _tenantAccess.IsSuperAdmin ? null : _tenantAccess.RequireBusinessId();
        var warehouse = await _warehouseRepository.GetByIdAsync(id, businessId);
        if (warehouse == null)
        {
            _logger.LogWarning("Warehouse update failed: Warehouse with ID {Id} not found", id);
            throw new InvalidOperationException($"Warehouse with ID {id} not found");
        }

        // Validate warehouse name uniqueness
        if (await _warehouseRepository.ExistsByNameAsync(dto.Name, id, businessId))
        {
            _logger.LogWarning("Warehouse update failed: Name '{Name}' already exists", dto.Name);
            throw new InvalidOperationException($"A warehouse with the name '{dto.Name}' already exists");
        }

        // Validate manager exists if provided
        if (dto.ManagerId.HasValue)
        {
            var manager = await _userRepository.GetByIdAsync(dto.ManagerId.Value, businessId);
            if (manager == null)
            {
                _logger.LogWarning("Warehouse update failed: Manager with ID {ManagerId} not found", dto.ManagerId);
                throw new InvalidOperationException($"Manager with ID {dto.ManagerId} not found");
            }
        }

        // Validate capacity is positive if provided
        if (dto.Capacity.HasValue && dto.Capacity.Value < 0)
        {
            throw new InvalidOperationException("Capacity must be a positive number");
        }

        warehouse.Name = dto.Name;
        warehouse.Address = dto.Address;
        warehouse.Capacity = dto.Capacity;
        warehouse.ManagerId = dto.ManagerId;

        var updatedWarehouse = await _warehouseRepository.UpdateAsync(warehouse);
        _logger.LogInformation("Warehouse updated: ID={Id}, Name={Name}", updatedWarehouse.Id, updatedWarehouse.Name);

        var poCount = await _warehouseRepository.GetPurchaseOrderCountAsync(id, businessId);
        return MapToDto(updatedWarehouse, poCount);
    }

    public async Task<bool> DeleteWarehouseAsync(long id)
    {
        long? businessId = _tenantAccess.IsSuperAdmin ? null : _tenantAccess.RequireBusinessId();
        var warehouse = await _warehouseRepository.GetByIdAsync(id, businessId);
        if (warehouse == null)
        {
            _logger.LogWarning("Warehouse deletion failed: Warehouse with ID {Id} not found", id);
            return false;
        }

        // Check if warehouse has purchase orders
        var poCount = await _warehouseRepository.GetPurchaseOrderCountAsync(id, businessId);
        if (poCount > 0)
        {
            _logger.LogWarning("Warehouse deletion failed: Warehouse {Id} has {Count} purchase orders", id, poCount);
            throw new InvalidOperationException($"Cannot delete warehouse. It has {poCount} purchase order(s)");
        }

        var deleted = await _warehouseRepository.DeleteAsync(id, businessId);
        if (deleted)
        {
            _logger.LogInformation("Warehouse deleted: ID={Id}, Name={Name}", id, warehouse.Name);
        }

        return deleted;
    }

    public async Task<IEnumerable<WarehouseDto>> SearchWarehousesAsync(string searchTerm)
    {
        long? businessId = _tenantAccess.IsSuperAdmin ? null : _tenantAccess.RequireBusinessId();
        var warehouses = await _warehouseRepository.SearchAsync(searchTerm, businessId);
        var warehouseDtos = new List<WarehouseDto>();

        foreach (var warehouse in warehouses)
        {
            var poCount = await _warehouseRepository.GetPurchaseOrderCountAsync(warehouse.Id, businessId);
            warehouseDtos.Add(MapToDto(warehouse, poCount));
        }

        return warehouseDtos;
    }

    public async Task<WarehouseStatsDto?> GetWarehouseStatsAsync(long id)
    {
        long? businessId = _tenantAccess.IsSuperAdmin ? null : _tenantAccess.RequireBusinessId();
        var warehouse = await _warehouseRepository.GetByIdAsync(id, businessId);
        if (warehouse == null)
            return null;

        var poCount = await _warehouseRepository.GetPurchaseOrderCountAsync(id, businessId);

        // TODO: Calculate stock and capacity utilization when Inventory module is implemented
        return new WarehouseStatsDto
        {
            Id = warehouse.Id,
            Name = warehouse.Name,
            Capacity = warehouse.Capacity,
            PurchaseOrderCount = poCount,
            CurrentStock = 0,
            CapacityUtilization = 0
        };
    }

    private static WarehouseDto MapToDto(Warehouse warehouse, int poCount)
    {
        return new WarehouseDto
        {
            Id = warehouse.Id,
            Name = warehouse.Name,
            Address = warehouse.Address,
            Capacity = warehouse.Capacity,
            ManagerId = warehouse.ManagerId,
            ManagerName = warehouse.Manager?.Name,
            PurchaseOrderCount = poCount,
            CreatedAt = warehouse.CreatedAt,
            UpdatedAt = warehouse.UpdatedAt
        };
    }
}
