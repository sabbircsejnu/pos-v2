using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.ReadModels.Mobile;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.ReadStores;

public sealed class MobileInventoryReadStore(RetailPOSDbContext dbContext)
    : IMobileInventoryReadStore
{
    public async Task<MobileInventoryPageReadModel> SearchOutletStockAsync(
        MobileInventoryCriteria criteria,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Inventories
            .AsNoTracking()
            .Where(inventory =>
                inventory.LocationType == "outlet"
                && inventory.LocationId == criteria.OutletId);

        if (criteria.ProductId.HasValue)
            query = query.Where(inventory => inventory.Variant.ProductId == criteria.ProductId.Value);

        if (criteria.VariantId.HasValue)
            query = query.Where(inventory => inventory.VariantId == criteria.VariantId.Value);

        if (criteria.LowStockOnly)
            query = query.Where(inventory => inventory.Quantity <= inventory.LowStockThreshold);

        if (!string.IsNullOrWhiteSpace(criteria.Search))
        {
            var search = criteria.Search.ToLowerInvariant();
            query = query.Where(inventory =>
                inventory.Variant.Product.Name.ToLower().Contains(search)
                || (inventory.Variant.Product.ProductCode != null
                    && inventory.Variant.Product.ProductCode.ToLower().Contains(search))
                || inventory.Variant.Sku.ToLower().Contains(search)
                || (inventory.Variant.Barcode != null
                    && inventory.Variant.Barcode.ToLower().Contains(search)));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var outletName = await dbContext.Outlets
            .AsNoTracking()
            .Where(outlet => outlet.Id == criteria.OutletId)
            .Select(outlet => outlet.Name)
            .SingleAsync(cancellationToken);
        var items = await query
            .OrderBy(inventory => inventory.Variant.Product.Name)
            .ThenBy(inventory => inventory.VariantId)
            .ThenBy(inventory => inventory.Id)
            .Skip((criteria.Page - 1) * criteria.PageSize)
            .Take(criteria.PageSize)
            .Select(inventory => new MobileInventoryItemReadModel(
                inventory.Id,
                inventory.Variant.ProductId,
                inventory.VariantId,
                inventory.Variant.Product.Name,
                inventory.Variant.Product.ProductCode,
                inventory.Variant.Name,
                inventory.Variant.Sku,
                inventory.Variant.Barcode,
                inventory.Variant.Attributes,
                criteria.OutletId,
                outletName,
                inventory.Quantity,
                inventory.LowStockThreshold,
                inventory.Quantity <= inventory.LowStockThreshold))
            .ToListAsync(cancellationToken);

        return new MobileInventoryPageReadModel(items, criteria.Page, criteria.PageSize, totalCount);
    }
}
