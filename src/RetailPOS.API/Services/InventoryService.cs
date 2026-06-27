using Microsoft.EntityFrameworkCore;
using RetailPOS.API.DTOs.Inventory;
using RetailPOS.Infrastructure.Repositories;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.API.Services;

/// <summary>
/// Service for inventory operations
/// </summary>
public class InventoryService : IInventoryService
{
    private readonly IInventoryRepository _inventoryRepository;
    private readonly RetailPOSDbContext _context;
    private readonly ILogger<InventoryService> _logger;

    public InventoryService(
        IInventoryRepository inventoryRepository,
        RetailPOSDbContext context,
        ILogger<InventoryService> logger)
    {
        _inventoryRepository = inventoryRepository;
        _context = context;
        _logger = logger;
    }

    public async Task<InventoryDto?> GetByIdAsync(long id)
    {
        var inventory = await _inventoryRepository.GetByIdAsync(id);
        return inventory != null ? await MapToDtoAsync(inventory) : null;
    }

    public async Task<List<InventoryDto>> GetAllAsync()
    {
        var inventories = await _inventoryRepository.GetAllAsync();
        var dtos = new List<InventoryDto>();
        foreach (var inv in inventories)
        {
            dtos.Add(await MapToDtoAsync(inv));
        }
        return dtos;
    }

    public async Task<List<InventoryDto>> GetByOutletAsync(long outletId)
    {
        var inventories = await _inventoryRepository.GetByOutletAsync(outletId);
        var dtos = new List<InventoryDto>();
        foreach (var inv in inventories)
        {
            dtos.Add(await MapToDtoAsync(inv));
        }
        return dtos;
    }

    public async Task<List<InventoryDto>> GetByWarehouseAsync(long warehouseId)
    {
        var inventories = await _inventoryRepository.GetByWarehouseAsync(warehouseId);
        var dtos = new List<InventoryDto>();
        foreach (var inv in inventories)
        {
            dtos.Add(await MapToDtoAsync(inv));
        }
        return dtos;
    }

    public async Task<List<LowStockDto>> GetLowStockAsync(long? outletId = null, long? warehouseId = null)
    {
        var inventories = await _inventoryRepository.GetLowStockAsync(outletId, warehouseId);
        var dtos = new List<LowStockDto>();
        
        foreach (var i in inventories)
        {
            var locationName = await GetLocationNameAsync(i.LocationId, i.LocationType);
            dtos.Add(new LowStockDto
            {
                InventoryId = i.Id,
                ProductVariantId = i.VariantId,
                LocationId = i.LocationId,
                ProductName = i.Variant?.Product?.Name ?? "Unknown",
                VariantName = i.Variant?.Name ?? "Default",
                Sku = i.Variant?.Sku,
                CurrentQuantity = i.Quantity,
                ReorderLevel = i.LowStockThreshold,
                LocationName = locationName,
                LocationType = i.LocationType == "outlet" ? "Outlet" : "Warehouse",
                LastRestockedAt = null // Not available in entity
            });
        }
        
        return dtos;
    }

    public async Task<List<InventoryDto>> GetOutOfStockAsync(long? outletId = null, long? warehouseId = null)
    {
        var inventories = await _inventoryRepository.GetOutOfStockAsync(outletId, warehouseId);
        var dtos = new List<InventoryDto>();
        foreach (var inv in inventories)
        {
            dtos.Add(await MapToDtoAsync(inv));
        }
        return dtos;
    }

    public async Task<List<InventoryDto>> GetExpiringSoonAsync(int days = 30, long? outletId = null, long? warehouseId = null)
    {
        var inventories = await _inventoryRepository.GetExpiringSoonAsync(days, outletId, warehouseId);
        var dtos = new List<InventoryDto>();
        foreach (var inv in inventories)
        {
            dtos.Add(await MapToDtoAsync(inv));
        }
        return dtos;
    }

    public async Task<List<InventoryDto>> SearchAsync(InventorySearchDto searchDto)
    {
        var inventories = await _inventoryRepository.SearchAsync(
            searchDto.ProductSearch,
            searchDto.VariantId,
            searchDto.VariantSearch,
            searchDto.CategoryId,
            searchDto.OutletId,
            searchDto.WarehouseId,
            searchDto.LowStockOnly,
            searchDto.OutOfStockOnly
        );

        // Additional filtering for expiring items
        if (searchDto.ExpiringSoon == true)
        {
            var days = searchDto.ExpiringWithinDays ?? 30;
            var threshold = DateTime.UtcNow.AddDays(days);
            inventories = inventories
                .Where(i => i.ExpiryDate.HasValue && i.ExpiryDate.Value <= threshold)
                .ToList();
        }

        // Apply pagination
        var paginatedInventories = inventories
            .Skip((searchDto.Page - 1) * searchDto.PageSize)
            .Take(searchDto.PageSize)
            .ToList();

        var dtos = new List<InventoryDto>();
        foreach (var inv in paginatedInventories)
        {
            dtos.Add(await MapToDtoAsync(inv));
        }
        return dtos;
    }

    public async Task<InventoryDto> UpdateStockThresholdAsync(long id, UpdateStockThresholdDto dto)
    {
        var inventory = await _inventoryRepository.GetByIdAsync(id);
        if (inventory == null)
            throw new KeyNotFoundException($"Inventory with ID {id} not found");

        inventory.LowStockThreshold = dto.ReorderLevel;

        await _inventoryRepository.UpdateAsync(inventory);

        _logger.LogInformation("Updated stock threshold for inventory {InventoryId}: Reorder={ReorderLevel}",
            id, dto.ReorderLevel);

        return await MapToDtoAsync(inventory);
    }

    public async Task<InventoryValuationDto> GetValuationAsync(long? outletId = null, long? warehouseId = null)
    {
        var query = _context.Inventories
            .Include(i => i.Variant)
                .ThenInclude(v => v.Product)
                    .ThenInclude(p => p.Category)
            .AsQueryable();

        string locationName = "All Locations";
        string locationType = "All";

        if (outletId.HasValue)
        {
            query = query.Where(i => i.LocationType.ToLower() == "outlet" && i.LocationId == outletId);
            var outlet = await _context.Outlets.FindAsync(outletId.Value);
            locationName = outlet?.Name ?? "Unknown Outlet";
            locationType = "Outlet";
        }
        else if (warehouseId.HasValue)
        {
            query = query.Where(i => i.LocationType.ToLower() == "warehouse" && i.LocationId == warehouseId);
            var warehouse = await _context.Warehouses.FindAsync(warehouseId.Value);
            locationName = warehouse?.Name ?? "Unknown Warehouse";
            locationType = "Warehouse";
        }

        var inventories = await query.ToListAsync();

        var categoryBreakdown = inventories
            .GroupBy(i => i.Variant?.Product?.Category?.Name ?? "Uncategorized")
            .Select(g => new CategoryValuationDto
            {
                CategoryName = g.Key,
                ProductCount = g.Select(i => i.VariantId).Distinct().Count(),
                TotalQuantity = g.Sum(i => i.Quantity),
                TotalCostValue = g.Sum(i => i.Quantity * (i.Variant?.Product?.CostPrice ?? 0 + i.Variant?.CostAdjustment ?? 0)),
                TotalRetailValue = g.Sum(i => i.Quantity * (i.Variant?.Product?.BasePrice ?? 0 + i.Variant?.PriceAdjustment ?? 0))
            })
            .OrderByDescending(c => c.TotalCostValue)
            .ToList();

        return new InventoryValuationDto
        {
            LocationName = locationName,
            LocationType = locationType,
            TotalItems = inventories.Count,
            TotalQuantity = inventories.Sum(i => i.Quantity),
            TotalCostValue = inventories.Sum(i => i.Quantity * ((i.Variant?.Product?.CostPrice ?? 0) + (i.Variant?.CostAdjustment ?? 0))),
            TotalRetailValue = inventories.Sum(i => i.Quantity * ((i.Variant?.Product?.BasePrice ?? 0) + (i.Variant?.PriceAdjustment ?? 0))),
            CategoryBreakdown = categoryBreakdown
        };
    }

    public async Task<List<InventoryByLocationDto>> GetInventorySummaryByLocationsAsync()
    {
        var inventories = await _context.Inventories
            .Include(i => i.Variant)
            .ToListAsync();

        var summaries = new List<InventoryByLocationDto>();

        // Group by outlets
        var outletGroups = inventories
            .Where(i => i.LocationType.Equals("outlet", StringComparison.OrdinalIgnoreCase))
            .GroupBy(i => i.LocationId);

        foreach (var group in outletGroups)
        {
            var outlet = await _context.Outlets.FindAsync(group.Key);
            summaries.Add(new InventoryByLocationDto
            {
                OutletId = group.Key,
                OutletName = outlet?.Name ?? "Unknown Outlet",
                LocationType = "Outlet",
                TotalProducts = group.Select(i => i.VariantId).Distinct().Count(),
                TotalQuantity = group.Sum(i => i.Quantity),
                LowStockCount = group.Count(i => i.Quantity <= i.LowStockThreshold),
                OutOfStockCount = group.Count(i => i.Quantity == 0),
                TotalValue = group.Sum(i => i.Quantity * ((i.Variant?.Product?.CostPrice ?? 0) + (i.Variant?.CostAdjustment ?? 0)))
            });
        }

        // Group by warehouses
        var warehouseGroups = inventories
            .Where(i => i.LocationType.Equals("warehouse", StringComparison.OrdinalIgnoreCase))
            .GroupBy(i => i.LocationId);

        foreach (var group in warehouseGroups)
        {
            var warehouse = await _context.Warehouses.FindAsync(group.Key);
            summaries.Add(new InventoryByLocationDto
            {
                WarehouseId = group.Key,
                WarehouseName = warehouse?.Name ?? "Unknown Warehouse",
                LocationType = "Warehouse",
                TotalProducts = group.Select(i => i.VariantId).Distinct().Count(),
                TotalQuantity = group.Sum(i => i.Quantity),
                LowStockCount = group.Count(i => i.Quantity <= i.LowStockThreshold),
                OutOfStockCount = group.Count(i => i.Quantity == 0),
                TotalValue = group.Sum(i => i.Quantity * ((i.Variant?.Product?.CostPrice ?? 0) + (i.Variant?.CostAdjustment ?? 0)))
            });
        }

        return summaries.OrderBy(s => s.LocationType).ThenBy(s => s.OutletName ?? s.WarehouseName).ToList();
    }

    public async Task<int> GetTotalStockByVariantAsync(long variantId)
    {
        return await _inventoryRepository.GetTotalQuantityByVariantAsync(variantId);
    }

    private async Task<InventoryDto> MapToDtoAsync(Core.Entities.Inventory inventory)
    {
        var locationName = await GetLocationNameAsync(inventory.LocationId, inventory.LocationType);
        
        var costPrice = (inventory.Variant?.Product?.CostPrice ?? 0) + (inventory.Variant?.CostAdjustment ?? 0);
        var retailPrice = (inventory.Variant?.Product?.BasePrice ?? 0) + (inventory.Variant?.PriceAdjustment ?? 0);
        
        return new InventoryDto
        {
            Id = inventory.Id,
            ProductId = inventory.Variant?.ProductId ?? 0,
            ProductVariantId = inventory.VariantId,
            ProductName = inventory.Variant?.Product?.Name ?? "Unknown",
            ProductCode = inventory.Variant?.Product?.ProductCode,
            VariantName = inventory.Variant?.Name ?? "Default",
            Sku = inventory.Variant?.Sku,
            Barcode = inventory.Variant?.Barcode,
            OutletId = inventory.LocationType.Equals("outlet", StringComparison.OrdinalIgnoreCase) ? inventory.LocationId : null,
            OutletName = inventory.LocationType.Equals("outlet", StringComparison.OrdinalIgnoreCase) ? locationName : null,
            WarehouseId = inventory.LocationType.Equals("warehouse", StringComparison.OrdinalIgnoreCase) ? inventory.LocationId : null,
            WarehouseName = inventory.LocationType.Equals("warehouse", StringComparison.OrdinalIgnoreCase) ? locationName : null,
            LocationType = inventory.LocationType.Equals("outlet", StringComparison.OrdinalIgnoreCase) ? "Outlet" : "Warehouse",
            Quantity = inventory.Quantity,
            ReorderLevel = inventory.LowStockThreshold,
            MaxStockLevel = null, // Not available in entity
            BatchNumber = inventory.BatchNumber,
            ExpiryDate = inventory.ExpiryDate,
            CostPrice = costPrice,
            RetailPrice = retailPrice,
            LastRestockedAt = null // Not available in entity
        };
    }

    private async Task<string> GetLocationNameAsync(long locationId, string locationType)
    {
        if (locationType.Equals("outlet", StringComparison.OrdinalIgnoreCase))
        {
            var outlet = await _context.Outlets.FindAsync(locationId);
            return outlet?.Name ?? "Unknown Outlet";
        }

        if (locationType.Equals("warehouse", StringComparison.OrdinalIgnoreCase))
        {
            var warehouse = await _context.Warehouses.FindAsync(locationId);
            return warehouse?.Name ?? "Unknown Warehouse";
        }

        return $"Unknown Location {locationId}";
    }
}
