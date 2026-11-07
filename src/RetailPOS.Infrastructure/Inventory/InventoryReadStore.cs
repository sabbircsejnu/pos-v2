using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Inventory;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.ReadModels;
using RetailPOS.Application.Inventory.ReadModels.Alerts;
using RetailPOS.Application.Inventory.Queries;
using RetailPOS.Infrastructure.Repositories;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.ReadStores;

public sealed class InventoryReadStore(
    IInventoryRepository inventoryRepository,
    RetailPOSDbContext context) : IInventoryReadStore
{
    public async Task<InventoryReadModel?> GetByIdAsync(long id, CancellationToken cancellationToken)
    {
        var inventory = await inventoryRepository.GetByIdAsync(id, cancellationToken);
        return inventory is null ? null : await MapAsync(inventory, cancellationToken);
    }

    public async Task<IReadOnlyList<InventoryReadModel>> GetAllAsync(CancellationToken cancellationToken)
    {
        var inventories = await inventoryRepository.GetAllAsync(cancellationToken);
        var result = new List<InventoryReadModel>(inventories.Count);
        foreach (var inventory in inventories)
            result.Add(await MapAsync(inventory, cancellationToken));

        return result;
    }

    public async Task<IReadOnlyList<InventoryReadModel>> GetByOutletAsync(
        long outletId,
        CancellationToken cancellationToken)
    {
        var inventories = await inventoryRepository.GetByOutletAsync(outletId, cancellationToken);
        var result = new List<InventoryReadModel>(inventories.Count);
        foreach (var inventory in inventories)
            result.Add(await MapAsync(inventory, cancellationToken));

        return result;
    }

    public async Task<IReadOnlyList<InventoryReadModel>> GetByWarehouseAsync(
        long warehouseId,
        CancellationToken cancellationToken)
    {
        var inventories = await inventoryRepository.GetByWarehouseAsync(warehouseId, cancellationToken);
        var result = new List<InventoryReadModel>(inventories.Count);
        foreach (var inventory in inventories)
            result.Add(await MapAsync(inventory, cancellationToken));

        return result;
    }

    public async Task<IReadOnlyList<LowStockReadModel>> GetLowStockAsync(
        InventoryLocationScope locationScope,
        CancellationToken cancellationToken)
    {
        var inventories = await inventoryRepository.GetLowStockAsync(locationScope, cancellationToken);
        var result = new List<LowStockReadModel>(inventories.Count);
        foreach (var inventory in inventories)
        {
            var productId = inventory.Variant?.ProductId;
            result.Add(new LowStockReadModel(
                inventory.Id,
                inventory.VariantId,
                inventory.LocationId,
                inventory.Variant?.Product?.Name ?? "Unknown",
                inventory.Variant?.Name ?? "Default",
                inventory.Variant?.Attributes,
                await GetPrimaryImageThumbAsync(productId, cancellationToken),
                inventory.Variant?.Sku,
                inventory.Quantity,
                inventory.LowStockThreshold,
                await GetLocationNameAsync(inventory.LocationId, inventory.LocationType, cancellationToken),
                inventory.LocationType == "outlet" ? "Outlet" : "Warehouse"));
        }

        return result;
    }

    public async Task<IReadOnlyList<InventoryReadModel>> GetOutOfStockAsync(
        InventoryLocationScope locationScope,
        CancellationToken cancellationToken)
    {
        var inventories = await inventoryRepository.GetOutOfStockAsync(locationScope, cancellationToken);
        var result = new List<InventoryReadModel>(inventories.Count);
        foreach (var inventory in inventories)
            result.Add(await MapAsync(inventory, cancellationToken));

        return result;
    }

    public async Task<IReadOnlyList<InventoryReadModel>> GetExpiringSoonAsync(
        int days,
        InventoryLocationScope locationScope,
        CancellationToken cancellationToken)
    {
        var inventories = await inventoryRepository.GetExpiringSoonAsync(
            days,
            locationScope,
            cancellationToken);
        var result = new List<InventoryReadModel>(inventories.Count);
        foreach (var inventory in inventories)
            result.Add(await MapAsync(inventory, cancellationToken));

        return result;
    }

    public async Task<IReadOnlyList<InventoryLocationSummaryReadModel>> GetSummaryByLocationAsync(
        CancellationToken cancellationToken)
    {
        var inventories = await context.Inventories
            .Include(inventory => inventory.Variant)
            .ToListAsync(cancellationToken);
        var summaries = new List<InventoryLocationSummaryReadModel>();

        var outletGroups = inventories
            .Where(inventory => inventory.LocationType.Equals("outlet", StringComparison.OrdinalIgnoreCase))
            .GroupBy(inventory => inventory.LocationId);
        foreach (var group in outletGroups)
        {
            var outletName = await context.Outlets
                .Where(outlet => outlet.Id == group.Key)
                .Select(outlet => outlet.Name)
                .FirstOrDefaultAsync(cancellationToken);
            summaries.Add(new InventoryLocationSummaryReadModel(
                group.Key,
                outletName ?? "Unknown Outlet",
                null,
                null,
                "Outlet",
                group.Select(inventory => inventory.VariantId).Distinct().Count(),
                group.Sum(inventory => inventory.Quantity),
                group.Count(inventory => inventory.Quantity <= inventory.LowStockThreshold),
                group.Count(inventory => inventory.Quantity == 0),
                group.Sum(inventory => inventory.Quantity
                    * ((inventory.Variant?.Product?.CostPrice ?? 0m) + (inventory.Variant?.CostAdjustment ?? 0m)))));
        }

        var warehouseGroups = inventories
            .Where(inventory => inventory.LocationType.Equals("warehouse", StringComparison.OrdinalIgnoreCase))
            .GroupBy(inventory => inventory.LocationId);
        foreach (var group in warehouseGroups)
        {
            var warehouseName = await context.Warehouses
                .Where(warehouse => warehouse.Id == group.Key)
                .Select(warehouse => warehouse.Name)
                .FirstOrDefaultAsync(cancellationToken);
            summaries.Add(new InventoryLocationSummaryReadModel(
                null,
                null,
                group.Key,
                warehouseName ?? "Unknown Warehouse",
                "Warehouse",
                group.Select(inventory => inventory.VariantId).Distinct().Count(),
                group.Sum(inventory => inventory.Quantity),
                group.Count(inventory => inventory.Quantity <= inventory.LowStockThreshold),
                group.Count(inventory => inventory.Quantity == 0),
                group.Sum(inventory => inventory.Quantity
                    * ((inventory.Variant?.Product?.CostPrice ?? 0m) + (inventory.Variant?.CostAdjustment ?? 0m)))));
        }

        return summaries
            .OrderBy(summary => summary.LocationType)
            .ThenBy(summary => summary.OutletName ?? summary.WarehouseName)
            .ToList();
    }

    public async Task<InventoryValuationReadModel> GetValuationAsync(
        InventoryLocationScope locationScope,
        CancellationToken cancellationToken)
    {
        var query = context.Inventories
            .Include(inventory => inventory.Variant)
                .ThenInclude(variant => variant!.Product)
                    .ThenInclude(product => product!.Category)
            .AsQueryable();

        var locationName = "All Locations";
        var locationType = "All";

        query = ApplyLocationScope(query, locationScope);
        if (locationScope.SelectedOutletId.HasValue)
        {
            locationName = await context.Outlets
                .Where(outlet => outlet.Id == locationScope.SelectedOutletId.Value)
                .Select(outlet => outlet.Name)
                .FirstOrDefaultAsync(cancellationToken) ?? "Unknown Outlet";
            locationType = "Outlet";
        }
        else if (locationScope.SelectedWarehouseId.HasValue)
        {
            locationName = await context.Warehouses
                .Where(warehouse => warehouse.Id == locationScope.SelectedWarehouseId.Value)
                .Select(warehouse => warehouse.Name)
                .FirstOrDefaultAsync(cancellationToken) ?? "Unknown Warehouse";
            locationType = "Warehouse";
        }

        var inventories = await query.ToListAsync(cancellationToken);
        var categoryBreakdown = inventories
            .GroupBy(inventory => inventory.Variant?.Product?.Category?.Name ?? "Uncategorized")
            .Select(group => new CategoryValuationReadModel(
                group.Key,
                group.Select(inventory => inventory.VariantId).Distinct().Count(),
                group.Sum(inventory => inventory.Quantity),
                group.Sum(inventory => inventory.Quantity
                    * (inventory.Variant?.Product?.CostPrice ?? 0m + inventory.Variant?.CostAdjustment ?? 0m)),
                group.Sum(inventory => inventory.Quantity
                    * (inventory.Variant?.Product?.BasePrice ?? 0m + inventory.Variant?.PriceAdjustment ?? 0m))))
            .OrderByDescending(category => category.TotalCostValue)
            .ToList();

        return new InventoryValuationReadModel(
            locationName,
            locationType,
            inventories.Count,
            inventories.Sum(inventory => inventory.Quantity),
            inventories.Sum(inventory => inventory.Quantity
                * ((inventory.Variant?.Product?.CostPrice ?? 0m) + (inventory.Variant?.CostAdjustment ?? 0m))),
            inventories.Sum(inventory => inventory.Quantity
                * ((inventory.Variant?.Product?.BasePrice ?? 0m) + (inventory.Variant?.PriceAdjustment ?? 0m))),
            categoryBreakdown);
    }

    public async Task<IReadOnlyList<VariantAttributeFilterReadModel>> GetVariantAttributeFilterOptionsAsync(
        InventoryLocationScope locationScope,
        CancellationToken cancellationToken)
    {
        var activeVariations = await context.Variations
            .Include(variation => variation.Options)
            .Where(variation => variation.IsActive)
            .OrderBy(variation => variation.DisplayOrder)
            .ToListAsync(cancellationToken);
        if (activeVariations.Count == 0)
            return Array.Empty<VariantAttributeFilterReadModel>();

        var activeOptionsByName = activeVariations.ToDictionary(
            variation => VariantAttributeJson.NormalizeKey(variation.Name),
            variation => variation.Options
                .Where(option => option.IsActive)
                .Select(option => option.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

        var inventoryQuery = context.Inventories
            .Include(inventory => inventory.Variant)
            .AsQueryable();
        inventoryQuery = ApplyLocationScope(inventoryQuery, locationScope);

        var inventories = await inventoryQuery.ToListAsync(cancellationToken);
        var usedValuesByAttribute = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var inventory in inventories)
        {
            foreach (var (attributeName, values) in VariantAttributeJson.Parse(inventory.Variant?.Attributes))
            {
                var normalizedName = VariantAttributeJson.NormalizeKey(attributeName);
                if (!activeOptionsByName.TryGetValue(normalizedName, out var activeOptions))
                    continue;

                foreach (var value in values)
                {
                    if (!activeOptions.Contains(value))
                        continue;

                    if (!usedValuesByAttribute.TryGetValue(normalizedName, out var usedValues))
                    {
                        usedValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        usedValuesByAttribute[normalizedName] = usedValues;
                    }

                    usedValues.Add(value);
                }
            }
        }

        var result = new List<VariantAttributeFilterReadModel>();
        foreach (var variation in activeVariations)
        {
            var normalizedName = VariantAttributeJson.NormalizeKey(variation.Name);
            if (!usedValuesByAttribute.TryGetValue(normalizedName, out var usedValues))
                continue;

            var orderedUsedOptions = variation.Options
                .Where(option => option.IsActive)
                .OrderBy(option => option.DisplayOrder)
                .Select(option => option.Name)
                .Where(usedValues.Contains)
                .ToArray();
            if (orderedUsedOptions.Length == 0)
                continue;

            result.Add(new VariantAttributeFilterReadModel(variation.Name, orderedUsedOptions));
        }

        return result;
    }

    public async Task<IReadOnlyList<InventoryReadModel>> SearchAsync(
        InventorySearchCriteria criteria,
        CancellationToken cancellationToken)
    {
        var inventories = await inventoryRepository.SearchAsync(
            criteria.ProductSearch,
            criteria.VariantId,
            criteria.VariantSearch,
            criteria.CategoryId,
            criteria.LocationScope,
            criteria.LowStockOnly,
            criteria.OutOfStockOnly,
            cancellationToken);

        if (criteria.ExpiringSoon == true)
        {
            var threshold = DateTime.UtcNow.AddDays(criteria.ExpiringWithinDays ?? 30);
            inventories = inventories
                .Where(inventory => inventory.ExpiryDate.HasValue && inventory.ExpiryDate.Value <= threshold)
                .ToList();
        }

        inventories = ApplyVariantAttributeFilters(inventories, criteria.VariantAttributeFilters);
        var page = inventories
            .Skip((criteria.Page - 1) * criteria.PageSize)
            .Take(criteria.PageSize)
            .ToList();
        var result = new List<InventoryReadModel>(page.Count);
        foreach (var inventory in page)
            result.Add(await MapAsync(inventory, cancellationToken));

        return result;
    }

    private static IQueryable<Core.Entities.Inventory> ApplyLocationScope(
        IQueryable<Core.Entities.Inventory> query,
        InventoryLocationScope locationScope) =>
        query.Where(inventory =>
            (inventory.LocationType.ToLower() == "outlet" &&
             locationScope.OutletIds.Contains(inventory.LocationId)) ||
            (inventory.LocationType.ToLower() == "warehouse" &&
             locationScope.WarehouseIds.Contains(inventory.LocationId)));

    private static List<RetailPOS.Core.Entities.Inventory> ApplyVariantAttributeFilters(
        List<RetailPOS.Core.Entities.Inventory> inventories,
        IReadOnlyList<VariantAttributeFilterCriteria>? filters)
    {
        if (filters is null || filters.Count == 0)
            return inventories;

        var normalizedFilters = filters
            .Where(filter => !string.IsNullOrWhiteSpace(filter.AttributeName)
                && filter.Values is { Count: > 0 })
            .Select(filter => new
            {
                AttributeName = VariantAttributeJson.NormalizeKey(filter.AttributeName),
                Values = filter.Values!
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value.Trim())
                    .ToHashSet(StringComparer.OrdinalIgnoreCase)
            })
            .Where(filter => filter.Values.Count > 0)
            .ToList();
        if (normalizedFilters.Count == 0)
            return inventories;

        return inventories.Where(inventory =>
        {
            var attributes = VariantAttributeJson.Parse(inventory.Variant?.Attributes);
            foreach (var filter in normalizedFilters)
            {
                var matchingAttribute = attributes.FirstOrDefault(attribute =>
                    VariantAttributeJson.NormalizeKey(attribute.Key)
                        .Equals(filter.AttributeName, StringComparison.OrdinalIgnoreCase));
                if (string.IsNullOrWhiteSpace(matchingAttribute.Key)
                    || !matchingAttribute.Value.Any(filter.Values.Contains))
                    return false;
            }

            return true;
        }).ToList();
    }

    private async Task<InventoryReadModel> MapAsync(
        RetailPOS.Core.Entities.Inventory inventory,
        CancellationToken cancellationToken)
    {
        var isOutlet = inventory.LocationType.Equals("outlet", StringComparison.OrdinalIgnoreCase);
        var productId = inventory.Variant?.ProductId;
        var locationName = await GetLocationNameAsync(inventory.LocationId, inventory.LocationType, cancellationToken);
        var primaryImageThumb = await GetPrimaryImageThumbAsync(productId, cancellationToken);
        var costPrice = (inventory.Variant?.Product?.CostPrice ?? 0m) + (inventory.Variant?.CostAdjustment ?? 0m);
        var retailPrice = (inventory.Variant?.Product?.BasePrice ?? 0m) + (inventory.Variant?.PriceAdjustment ?? 0m);

        return new InventoryReadModel(
            inventory.Id,
            productId ?? 0,
            inventory.VariantId,
            inventory.Variant?.Product?.Name ?? "Unknown",
            inventory.Variant?.Product?.ProductCode,
            inventory.Variant?.Name ?? "Default",
            inventory.Variant?.Attributes,
            primaryImageThumb,
            inventory.Variant?.Sku,
            inventory.Variant?.Barcode,
            isOutlet ? inventory.LocationId : null,
            isOutlet ? locationName : null,
            isOutlet ? null : inventory.LocationId,
            isOutlet ? null : locationName,
            isOutlet ? "Outlet" : "Warehouse",
            inventory.Quantity,
            inventory.LowStockThreshold,
            inventory.BatchNumber,
            inventory.ExpiryDate,
            costPrice,
            retailPrice);
    }

    private async Task<string> GetLocationNameAsync(
        long locationId,
        string locationType,
        CancellationToken cancellationToken)
    {
        if (locationType.Equals("outlet", StringComparison.OrdinalIgnoreCase))
        {
            return await context.Outlets
                .Where(outlet => outlet.Id == locationId)
                .Select(outlet => outlet.Name)
                .FirstOrDefaultAsync(cancellationToken) ?? "Unknown Outlet";
        }

        if (locationType.Equals("warehouse", StringComparison.OrdinalIgnoreCase))
        {
            return await context.Warehouses
                .Where(warehouse => warehouse.Id == locationId)
                .Select(warehouse => warehouse.Name)
                .FirstOrDefaultAsync(cancellationToken) ?? "Unknown Warehouse";
        }

        return $"Unknown Location {locationId}";
    }

    private Task<string?> GetPrimaryImageThumbAsync(
        long? productId,
        CancellationToken cancellationToken) =>
        productId.HasValue
            ? context.ProductImages
                .Where(image => image.ProductId == productId.Value && image.IsPrimary)
                .Select(image => image.ThumbPath)
                .FirstOrDefaultAsync(cancellationToken)
            : Task.FromResult<string?>(null);

    public Task<decimal> GetTotalQuantityByVariantAsync(
        long variantId,
        CancellationToken cancellationToken) =>
        inventoryRepository.GetTotalQuantityByVariantAsync(variantId, cancellationToken);
}
