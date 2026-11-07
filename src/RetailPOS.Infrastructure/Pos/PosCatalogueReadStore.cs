using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Abstractions;
using RetailPOS.Application.Pos.Abstractions;
using RetailPOS.Application.Pos.ReadModels;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Pos;

/// <summary>Sellable variants for the POS browser: always restricted to the caller's Business and active products.</summary>
public sealed class PosCatalogueReadStore(RetailPOSDbContext dbContext, IBusinessScope businessScope) : IPosCatalogueReadStore
{
    public async Task<IReadOnlyList<PosCatalogueItemReadModel>> ListSellableVariantsAsync(
        PosCatalogueCriteria criteria,
        CancellationToken cancellationToken)
    {
        var businessId = businessScope.RequireBusinessId();
        var query = dbContext.ProductVariants
            .AsNoTracking()
            .Where(variant =>
                variant.BusinessId == businessId &&
                variant.Product.BusinessId == businessId &&
                variant.Product.Status == ProductStatus.Active);

        if (criteria.CategoryId.HasValue)
        {
            query = query.Where(variant => variant.Product.CategoryId == criteria.CategoryId.Value);
        }

        if (!string.IsNullOrWhiteSpace(criteria.Search))
        {
            var term = criteria.Search.Trim().ToLower();
            query = query.Where(variant =>
                variant.Product.Name.ToLower().Contains(term) ||
                variant.Name.ToLower().Contains(term) ||
                variant.Sku.ToLower().Contains(term) ||
                (variant.Barcode != null && variant.Barcode.ToLower().Contains(term)) ||
                (variant.Product.Barcode != null && variant.Product.Barcode.ToLower().Contains(term)) ||
                (variant.Product.ProductCode != null && variant.Product.ProductCode.ToLower().Contains(term)));
        }

        var outletId = criteria.OutletId;
        return await query
            .OrderBy(variant => variant.Product.Name)
            .ThenBy(variant => variant.Name)
            .ThenBy(variant => variant.Id)
            .Take(criteria.Limit)
            .Select(variant => new PosCatalogueItemReadModel(
                variant.Id,
                variant.ProductId,
                variant.Product.Name,
                variant.Name,
                variant.Product.ProductCode,
                variant.Sku,
                variant.Barcode ?? variant.Product.Barcode,
                variant.Attributes,
                variant.Product.CategoryId,
                variant.Product.Category.Name,
                variant.Product.BasePrice + variant.PriceAdjustment,
                variant.Product.Images
                    .Where(image => image.IsPrimary)
                    .Select(image => image.ThumbPath)
                    .FirstOrDefault(),
                outletId == null
                    ? null
                    : variant.Inventories
                        .Where(inventory =>
                            inventory.LocationId == outletId &&
                            inventory.LocationType == "outlet")
                        .Select(inventory => (decimal?)inventory.Quantity)
                        .FirstOrDefault() ?? 0))
            .ToListAsync(cancellationToken);
    }
}
