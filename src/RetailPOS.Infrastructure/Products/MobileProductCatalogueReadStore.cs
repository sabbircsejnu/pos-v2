using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Products.Abstractions;
using RetailPOS.Application.Products.ReadModels;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Products;

public sealed class MobileProductCatalogueReadStore(RetailPOSDbContext dbContext)
    : IMobileProductCatalogueReadStore
{
    private sealed record ProductProjection(
        long Id,
        string Name,
        string? Description,
        string? ProductCode,
        string? Sku,
        string? ProductBarcode,
        long CategoryId,
        string CategoryName,
        ProductStatus Status,
        bool HasVariants,
        int VariantCount);

    private sealed record VariantProjection(
        long Id,
        long ProductId,
        string Name,
        string Sku,
        string? Barcode,
        string Attributes);

    public async Task<MobileProductCategoryPageReadModel> ListCategoriesAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Categories
            .AsNoTracking()
            .Where(category => category.IsActive);
        var totalCount = await query.CountAsync(cancellationToken);
        var categories = await query
            .OrderBy(category => category.DisplayOrder)
            .ThenBy(category => category.Name)
            .ThenBy(category => category.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(category => new MobileProductCategoryReadModel(
                category.Id,
                category.Name,
                category.ParentCategoryId))
            .ToListAsync(cancellationToken);

        return new MobileProductCategoryPageReadModel(categories, page, pageSize, totalCount);
    }

    public async Task<MobileProductCataloguePageReadModel> SearchAsync(
        MobileProductCatalogueCriteria criteria,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Products
            .AsNoTracking()
            .Where(product => product.Status == criteria.Status);

        if (criteria.CategoryId.HasValue)
            query = query.Where(product => product.CategoryId == criteria.CategoryId.Value);

        if (!string.IsNullOrWhiteSpace(criteria.Search))
        {
            var search = criteria.Search.ToLowerInvariant();
            query = query.Where(product =>
                product.Name.ToLower().Contains(search)
                || (product.Description != null && product.Description.ToLower().Contains(search))
                || (product.ProductCode != null && product.ProductCode.ToLower().Contains(search))
                || (product.Sku != null && product.Sku.ToLower().Contains(search))
                || (product.Barcode != null && product.Barcode.ToLower().Contains(search))
                || product.ProductVariants.Any(variant =>
                    variant.Sku.ToLower().Contains(search)
                    || (variant.Barcode != null && variant.Barcode.ToLower().Contains(search))));
        }

        if (criteria.AttributeName is not null && criteria.AttributeValue is not null)
        {
            var attributeFilter = JsonSerializer.Serialize(
                new Dictionary<string, string>
                {
                    [criteria.AttributeName] = criteria.AttributeValue
                });
            query = query.Where(product => product.ProductVariants.Any(variant =>
                EF.Functions.JsonContains(variant.Attributes, attributeFilter)));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var products = await query
            .OrderBy(product => product.Name)
            .ThenBy(product => product.Id)
            .Skip((criteria.Page - 1) * criteria.PageSize)
            .Take(criteria.PageSize)
            .Select(product => new ProductProjection(
                product.Id,
                product.Name,
                product.Description,
                product.ProductCode,
                product.Sku,
                product.Barcode,
                product.CategoryId,
                product.Category.Name,
                product.Status,
                product.HasVariants,
                product.ProductVariants.Count))
            .ToListAsync(cancellationToken);

        return new MobileProductCataloguePageReadModel(
            products.Select(product => new MobileProductSummaryReadModel(
                product.Id,
                product.Name,
                product.Description,
                product.ProductCode,
                product.Sku,
                product.ProductBarcode,
                product.CategoryId,
                product.CategoryName,
                product.Status.ToString(),
                product.HasVariants,
                product.VariantCount)).ToArray(),
            criteria.Page,
            criteria.PageSize,
            totalCount);
    }

    public async Task<MobileProductReadModel?> FindProductAsync(
        long productId,
        CancellationToken cancellationToken)
    {
        var product = await dbContext.Products
            .AsNoTracking()
            .Where(candidate => candidate.Id == productId)
            .Select(candidate => new ProductProjection(
                candidate.Id,
                candidate.Name,
                candidate.Description,
                candidate.ProductCode,
                candidate.Sku,
                candidate.Barcode,
                candidate.CategoryId,
                candidate.Category.Name,
                candidate.Status,
                candidate.HasVariants,
                0))
            .SingleOrDefaultAsync(cancellationToken);

        return product is null
            ? null
            : ToProductReadModel(product);
    }

    public async Task<MobileProductVariantPageReadModel?> FindVariantsAsync(
        long productId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (!await dbContext.Products.AsNoTracking()
                .AnyAsync(product => product.Id == productId, cancellationToken))
            return null;

        var query = dbContext.ProductVariants
            .AsNoTracking()
            .Where(variant => variant.ProductId == productId);
        var totalCount = await query.CountAsync(cancellationToken);
        var variants = await query
            .OrderBy(variant => variant.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(variant => new VariantProjection(
                variant.Id,
                variant.ProductId,
                variant.Name,
                variant.Sku,
                variant.Barcode,
                variant.Attributes))
            .ToListAsync(cancellationToken);

        return new MobileProductVariantPageReadModel(
            variants.Select(ToVariantReadModel).ToArray(),
            page,
            pageSize,
            totalCount);
    }

    public async Task<MobileProductVariantReadModel?> FindVariantAsync(
        long productId,
        long variantId,
        CancellationToken cancellationToken)
    {
        var variant = await dbContext.ProductVariants
            .AsNoTracking()
            .Where(candidate => candidate.ProductId == productId && candidate.Id == variantId)
            .Select(candidate => new VariantProjection(
                candidate.Id,
                candidate.ProductId,
                candidate.Name,
                candidate.Sku,
                candidate.Barcode,
                candidate.Attributes))
            .SingleOrDefaultAsync(cancellationToken);

        return variant is null ? null : ToVariantReadModel(variant);
    }

    public async Task<IReadOnlyList<MobileProductBarcodeMatchReadModel>> FindBarcodeMatchesAsync(
        string barcode,
        CancellationToken cancellationToken)
    {
        var products = await dbContext.Products
            .AsNoTracking()
            .Where(product => product.Status == ProductStatus.Active && product.Barcode == barcode)
            .OrderBy(product => product.Id)
            .Take(2)
            .Select(product => new ProductProjection(
                product.Id,
                product.Name,
                product.Description,
                product.ProductCode,
                product.Sku,
                product.Barcode,
                product.CategoryId,
                product.Category.Name,
                product.Status,
                product.HasVariants,
                0))
            .ToListAsync(cancellationToken);

        var variants = await dbContext.ProductVariants
            .AsNoTracking()
            .Where(variant => variant.Product.Status == ProductStatus.Active && variant.Barcode == barcode)
            .OrderBy(variant => variant.Id)
            .Take(2)
            .Select(variant => new
            {
                Product = new ProductProjection(
                    variant.Product.Id,
                    variant.Product.Name,
                    variant.Product.Description,
                    variant.Product.ProductCode,
                    variant.Product.Sku,
                    variant.Product.Barcode,
                    variant.Product.CategoryId,
                    variant.Product.Category.Name,
                    variant.Product.Status,
                    variant.Product.HasVariants,
                    0),
                Variant = new VariantProjection(
                    variant.Id,
                    variant.ProductId,
                    variant.Name,
                    variant.Sku,
                    variant.Barcode,
                    variant.Attributes)
            })
            .ToListAsync(cancellationToken);

        var matches = new List<MobileProductBarcodeMatchReadModel>(products.Count + variants.Count);
        matches.AddRange(products.Select(product => new MobileProductBarcodeMatchReadModel(
            ToProductReadModel(product),
            null,
            "product")));
        matches.AddRange(variants.Select(match => new MobileProductBarcodeMatchReadModel(
            ToProductReadModel(match.Product),
            ToVariantReadModel(match.Variant),
            "variant")));
        return matches;
    }

    private static MobileProductReadModel ToProductReadModel(ProductProjection product) =>
        new(
            product.Id,
            product.Name,
            product.Description,
            product.ProductCode,
            product.Sku,
            product.ProductBarcode,
            product.CategoryId,
            product.CategoryName,
            product.Status.ToString(),
            product.HasVariants);

    private static MobileProductVariantReadModel ToVariantReadModel(VariantProjection variant) =>
        new(
            variant.Id,
            variant.ProductId,
            variant.Name,
            variant.Sku,
            variant.Barcode,
            variant.Attributes);
}
