using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Abstractions;
using RetailPOS.Application.Products.Abstractions;
using RetailPOS.Application.Products.ReadModels;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Repositories;
using System.Linq.Expressions;

namespace RetailPOS.Infrastructure.Products;

public sealed class ProductReadStore(
    RetailPOSDbContext dbContext,
    IProductRepository productRepository,
    IProductVariantRepository productVariantRepository,
    IInventoryRepository inventoryRepository,
    IBusinessScope businessScope) : IProductReadStore
{
    public async Task<ProductReadModel?> FindByIdAsync(
        long productId,
        CancellationToken cancellationToken)
        => await FindAsync(candidate => candidate.Id == productId, cancellationToken);

    public async Task<ProductReadModel?> FindBySkuAsync(
        string sku,
        CancellationToken cancellationToken)
        => await FindAsync(candidate => candidate.Sku == sku, cancellationToken);

    public async Task<ProductReadModel?> FindByBarcodeAsync(
        string barcode,
        CancellationToken cancellationToken)
        => await FindAsync(candidate => candidate.Barcode == barcode, cancellationToken);

    private async Task<ProductReadModel?> FindAsync(
        Expression<Func<Product, bool>> predicate,
        CancellationToken cancellationToken)
    {
        var businessId = businessScope.RequireBusinessId();
        var product = await dbContext.Products
            .AsNoTracking()
            .Where(candidate => candidate.BusinessId == businessId)
            .Include(candidate => candidate.Category)
            .Include(candidate => candidate.ProductVariants)
            .Include(candidate => candidate.Images.Where(image => image.IsPrimary))
            .FirstOrDefaultAsync(predicate, cancellationToken);
        if (product is null)
        {
            return null;
        }

        return MapToReadModel(product);
    }

    public Task<IReadOnlyList<ProductReadModel>> ListAllAsync(CancellationToken cancellationToken) =>
        ListAsync(WithDetails().OrderBy(candidate => candidate.Name), cancellationToken);

    public Task<IReadOnlyList<ProductReadModel>> ListByCategoryAsync(
        long categoryId,
        CancellationToken cancellationToken) =>
        ListAsync(
            WithDetails()
                .Where(candidate => candidate.CategoryId == categoryId)
                .OrderBy(candidate => candidate.Name),
            cancellationToken);

    public async Task<ProductSearchReadModel> SearchAsync(
        ProductSearchCriteria criteria,
        CancellationToken cancellationToken)
    {
        var query = WithDetails();
        if (!string.IsNullOrWhiteSpace(criteria.SearchQuery))
        {
            var lowerSearchQuery = criteria.SearchQuery.ToLower();
            query = query.Where(candidate =>
                candidate.Name.ToLower().Contains(lowerSearchQuery) ||
                (candidate.Description != null && candidate.Description.ToLower().Contains(lowerSearchQuery)) ||
                (candidate.ProductCode != null && candidate.ProductCode.ToLower().Contains(lowerSearchQuery)) ||
                (candidate.Sku != null && candidate.Sku.ToLower().Contains(lowerSearchQuery)) ||
                (candidate.Barcode != null && candidate.Barcode.ToLower().Contains(lowerSearchQuery)));
        }

        if (criteria.CategoryId.HasValue)
        {
            query = query.Where(candidate => candidate.CategoryId == criteria.CategoryId.Value);
        }

        var status = ParseStatus(criteria.Status);
        if (status.HasValue)
        {
            query = query.Where(candidate => candidate.Status == status.Value);
        }

        if (criteria.HasVariants.HasValue)
        {
            query = query.Where(candidate => candidate.HasVariants == criteria.HasVariants.Value);
        }

        if (criteria.MinPrice.HasValue)
        {
            query = query.Where(candidate => candidate.BasePrice >= criteria.MinPrice.Value);
        }

        if (criteria.MaxPrice.HasValue)
        {
            query = query.Where(candidate => candidate.BasePrice <= criteria.MaxPrice.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        query = ApplySorting(query, criteria.SortBy, criteria.SortOrder);

        var products = await query
            .Skip((criteria.PageNumber - 1) * criteria.PageSize)
            .Take(criteria.PageSize)
            .ToListAsync(cancellationToken);
        var results = new List<ProductReadModel>(products.Count);
        foreach (var product in products)
        {
            results.Add(MapToReadModel(product));
        }

        return new ProductSearchReadModel(
            results,
            totalCount,
            criteria.PageNumber,
            criteria.PageSize);
    }

    public async Task<IReadOnlyList<ProductVariantSearchReadModel>> SearchVariantsAsync(
        ProductVariantSearchCriteria criteria,
        CancellationToken cancellationToken)
    {
        var variants = await productVariantRepository.SearchAsync(
            criteria.Query,
            criteria.PageNumber,
            criteria.PageSize);
        var results = new List<ProductVariantSearchReadModel>();
        var normalizedLocationType = string.IsNullOrWhiteSpace(criteria.LocationType)
            ? null
            : criteria.LocationType.Trim().ToLowerInvariant();

        foreach (var variant in variants)
        {
            var product = variant.Product ?? await productRepository.GetByIdAsync(variant.ProductId);
            if (product is null)
            {
                continue;
            }

            var displayName = variant.Name;
            if (string.IsNullOrWhiteSpace(displayName) ||
                displayName.Equals("Default", StringComparison.OrdinalIgnoreCase))
            {
                displayName = "Standard";
            }

            decimal? stockQuantity = null;
            if (criteria.LocationId.HasValue &&
                (normalizedLocationType == "outlet" || normalizedLocationType == "warehouse"))
            {
                var inventory = await inventoryRepository.GetByVariantAndLocationAsync(
                    variant.Id,
                    criteria.LocationId.Value,
                    normalizedLocationType);
                stockQuantity = inventory?.Quantity ?? 0;
            }

            results.Add(new ProductVariantSearchReadModel(
                variant.Id,
                variant.ProductId,
                product.Name,
                product.ProductCode,
                displayName,
                variant.Sku,
                variant.Barcode,
                variant.Attributes,
                product.BasePrice + variant.PriceAdjustment,
                product.CostPrice,
                stockQuantity,
                product.Images?.FirstOrDefault(image => image.IsPrimary)?.ThumbPath));
        }

        return results;
    }

    private async Task<IReadOnlyList<ProductReadModel>> ListAsync(
        IQueryable<Product> productQuery,
        CancellationToken cancellationToken)
    {
        var products = await productQuery.ToListAsync(cancellationToken);
        var results = new List<ProductReadModel>(products.Count);
        foreach (var product in products)
        {
            results.Add(MapToReadModel(product));
        }

        return results;
    }

    private IQueryable<Product> WithDetails()
    {
        var businessId = businessScope.RequireBusinessId();
        return dbContext.Products
            .AsNoTracking()
            .Where(candidate => candidate.BusinessId == businessId)
            .Include(candidate => candidate.Category)
            .Include(candidate => candidate.ProductVariants)
            .Include(candidate => candidate.Images.Where(image => image.IsPrimary));
    }

    private static IQueryable<Product> ApplySorting(
        IQueryable<Product> query,
        string sortBy,
        string sortOrder) =>
        sortBy.ToLower() switch
        {
            "name" => sortOrder.ToLower() == "desc"
                ? query.OrderByDescending(product => product.Name)
                : query.OrderBy(product => product.Name),
            "price" => sortOrder.ToLower() == "desc"
                ? query.OrderByDescending(product => product.BasePrice)
                : query.OrderBy(product => product.BasePrice),
            "category" => sortOrder.ToLower() == "desc"
                ? query.OrderByDescending(product => product.Category.Name)
                : query.OrderBy(product => product.Category.Name),
            "createdat" => sortOrder.ToLower() == "desc"
                ? query.OrderByDescending(product => product.CreatedAt)
                : query.OrderBy(product => product.CreatedAt),
            _ => query.OrderBy(product => product.Name)
        };

    private static RetailPOS.Core.Entities.ProductStatus? ParseStatus(string? status) =>
        status?.ToLower() switch
        {
            "active" => RetailPOS.Core.Entities.ProductStatus.Active,
            "inactive" => RetailPOS.Core.Entities.ProductStatus.Inactive,
            "draft" => RetailPOS.Core.Entities.ProductStatus.Draft,
            _ => null
        };

    private static ProductReadModel MapToReadModel(Product product)
    {
        var variants = product.ProductVariants
            .Select(variant => new ProductVariantReadModel(
                variant.Id,
                variant.ProductId,
                product.ProductCode,
                variant.Name,
                variant.Sku,
                variant.Barcode,
                variant.Attributes,
                variant.PriceAdjustment,
                product.BasePrice + variant.PriceAdjustment,
                variant.CreatedAt,
                variant.UpdatedAt))
            .ToArray();
        var primaryImage = product.Images.FirstOrDefault(image => image.IsPrimary);
        // Stock is enriched in the list/detail query handlers in one scoped batch.
        var totalStock = 0;

        return new ProductReadModel(
            product.Id,
            product.Name,
            product.Description,
            product.ProductCode,
            product.Sku,
            product.Barcode,
            product.CategoryId,
            product.Category?.Name ?? string.Empty,
            product.BasePrice,
            product.CostPrice,
            product.TaxRate,
            product.TaxClassification,
            product.HasVariants,
            primaryImage?.ThumbPath,
            primaryImage?.MediumPath,
            product.Status.ToString().ToLowerInvariant(),
            product.IsActive,
            variants.Length,
            totalStock,
            variants,
            product.CreatedAt,
            product.UpdatedAt);
    }
}
