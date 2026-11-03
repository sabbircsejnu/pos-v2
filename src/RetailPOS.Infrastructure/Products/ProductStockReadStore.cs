using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Products.Abstractions;
using RetailPOS.Application.Products.ReadModels;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Products;

public sealed class ProductStockReadStore(
    RetailPOSDbContext dbContext,
    IInventoryReportReadScopeProvider scopeProvider) : IProductStockReadStore
{
    public async Task<IReadOnlyList<ProductReadModel>> EnrichAsync(
        IReadOnlyList<ProductReadModel> products,
        long? outletId,
        long? warehouseId,
        CancellationToken cancellationToken)
    {
        if (outletId.HasValue && warehouseId.HasValue || outletId <= 0 || warehouseId <= 0)
            throw new ArgumentException("Select either a valid outlet or a valid warehouse.");

        var scope = await scopeProvider.GetAsync(cancellationToken);
        var outlets = dbContext.Outlets.AsNoTracking()
            .Where(location => scope.OutletIds.Contains(location.Id)
                && (!scope.BusinessId.HasValue || location.BusinessId == scope.BusinessId));
        var warehouses = dbContext.Warehouses.AsNoTracking()
            .Where(location => scope.WarehouseIds.Contains(location.Id)
                && (!scope.BusinessId.HasValue || location.BusinessId == scope.BusinessId));

        if (outletId.HasValue && !await outlets.AnyAsync(location => location.Id == outletId, cancellationToken)
            || warehouseId.HasValue && !await warehouses.AnyAsync(location => location.Id == warehouseId, cancellationToken))
            throw new UnauthorizedAccessException("The requested inventory location is not authorized.");

        if (products.Count == 0)
            return products;

        var productIds = products.Select(product => product.Id).ToList();
        var quantities = await dbContext.Inventories.AsNoTracking()
            .Where(inventory => productIds.Contains(inventory.Variant.ProductId))
            .Where(inventory =>
                inventory.LocationType == "outlet"
                    && outlets.Any(location => location.Id == inventory.LocationId)
                    && (!outletId.HasValue || inventory.LocationId == outletId)
                    && !warehouseId.HasValue
                || inventory.LocationType == "warehouse"
                    && warehouses.Any(location => location.Id == inventory.LocationId)
                    && (!warehouseId.HasValue || inventory.LocationId == warehouseId)
                    && !outletId.HasValue)
            .GroupBy(inventory => inventory.VariantId)
            .Select(group => new { VariantId = group.Key, Quantity = group.Sum(inventory => inventory.Quantity) })
            .ToDictionaryAsync(row => row.VariantId, row => row.Quantity, cancellationToken);

        return products.Select(product =>
        {
            // Variants currently have neither an activity flag nor a stock-tracking opt-out.
            // Missing inventory is distinct from an explicitly initialized zero balance.
            var known = product.Variants.Count(variant => quantities.ContainsKey(variant.Id));
            var stocked = product.Variants.Count(variant => quantities.TryGetValue(variant.Id, out var quantity) && quantity > 0);
            var status = stocked > 0
                ? stocked == product.Variants.Count ? "in_stock" : "partially_in_stock"
                : known > 0 && known == product.Variants.Count ? "out_of_stock" : "not_initialized";
            return product with
            {
                TotalStock = product.Variants.Sum(variant => quantities.GetValueOrDefault(variant.Id)),
                StockStatus = status
            };
        }).ToArray();
    }
}
