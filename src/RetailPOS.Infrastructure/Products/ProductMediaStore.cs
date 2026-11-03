using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Products.Abstractions;
using RetailPOS.Application.Products.ReadModels;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Products;

public sealed class ProductMediaStore(RetailPOSDbContext dbContext) : IProductMediaStore
{
    public async Task<IReadOnlyList<ProductImageReadModel>> ListAsync(
        long productId,
        CancellationToken cancellationToken) =>
        await dbContext.ProductImages
            .Where(image => image.ProductId == productId)
            .OrderByDescending(image => image.IsPrimary)
            .ThenBy(image => image.SortOrder)
            .ThenBy(image => image.Id)
            .Select(image => new ProductImageReadModel(
                image.Id,
                image.ProductId,
                image.OriginalName,
                image.MimeType,
                image.Size,
                image.Width,
                image.Height,
                image.ThumbPath,
                image.MediumPath,
                image.OriginalPath,
                image.IsPrimary,
                image.SortOrder,
                image.CreatedAt,
                image.UpdatedAt))
            .ToListAsync(cancellationToken);

    public async Task EnsureProductExistsAsync(long productId, CancellationToken cancellationToken)
    {
        var productExists = await dbContext.Products
            .AnyAsync(product => product.Id == productId, cancellationToken);
        if (!productExists)
            throw new KeyNotFoundException($"Product {productId} not found.");
    }

    public async Task<PendingProductImageUpload> CreatePendingUploadAsync(
        long productId,
        ProductImageUploadMetadata metadata,
        CancellationToken cancellationToken)
    {
        var hasExisting = await dbContext.ProductImages
            .AnyAsync(image => image.ProductId == productId, cancellationToken);
        var sortOrder = await dbContext.ProductImages
            .Where(image => image.ProductId == productId)
            .Select(image => (int?)image.SortOrder)
            .MaxAsync(cancellationToken) ?? -1;

        var entity = new ProductImage
        {
            ProductId = productId,
            OriginalName = metadata.OriginalName,
            MimeType = metadata.MimeType,
            Size = metadata.Size,
            Width = metadata.Width,
            Height = metadata.Height,
            OriginalPath = "",
            MediumPath = "",
            ThumbPath = "",
            IsPrimary = false,
            SortOrder = sortOrder + 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        dbContext.ProductImages.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new PendingProductImageUpload(entity.Id, metadata.IsPrimary || !hasExisting);
    }

    public async Task<ProductImageReadModel> CompleteUploadAsync(
        long productId,
        long imageId,
        ProductImageFilePaths paths,
        bool makePrimary,
        CancellationToken cancellationToken)
    {
        var entity = await dbContext.ProductImages
            .FirstOrDefaultAsync(
                image => image.Id == imageId && image.ProductId == productId,
                cancellationToken)
            ?? throw new KeyNotFoundException($"Image {imageId} not found for product {productId}.");

        entity.OriginalPath = paths.OriginalPath;
        entity.MediumPath = paths.MediumPath;
        entity.ThumbPath = paths.ThumbPath;
        if (makePrimary)
        {
            await ClearOtherPrimariesAsync(productId, imageId, cancellationToken);
            entity.IsPrimary = true;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return ToReadModel(entity);
    }

    public async Task<ProductImageDeletion> DeleteAsync(
        long productId,
        long imageId,
        CancellationToken cancellationToken)
    {
        var image = await dbContext.ProductImages
            .FirstOrDefaultAsync(
                item => item.Id == imageId && item.ProductId == productId,
                cancellationToken)
            ?? throw new KeyNotFoundException($"Image {imageId} not found for product {productId}.");

        var deletion = new ProductImageDeletion(
            image.OriginalPath,
            image.MediumPath,
            image.ThumbPath,
            image.IsPrimary);
        dbContext.ProductImages.Remove(image);
        await dbContext.SaveChangesAsync(cancellationToken);
        return deletion;
    }

    public async Task PromoteNextPrimaryAsync(long productId, CancellationToken cancellationToken)
    {
        var next = await dbContext.ProductImages
            .Where(image => image.ProductId == productId)
            .OrderBy(image => image.SortOrder)
            .ThenBy(image => image.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (next is null)
            return;

        next.IsPrimary = true;
        next.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<long> SetPrimaryAsync(
        long productId,
        long imageId,
        CancellationToken cancellationToken)
    {
        var target = await dbContext.ProductImages
            .FirstOrDefaultAsync(
                image => image.Id == imageId && image.ProductId == productId,
                cancellationToken)
            ?? throw new KeyNotFoundException($"Image {imageId} not found for product {productId}.");

        if (target.IsPrimary)
            return target.Id;

        await ClearOtherPrimariesAsync(productId, imageId, cancellationToken);
        target.IsPrimary = true;
        target.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return target.Id;
    }

    private async Task ClearOtherPrimariesAsync(
        long productId,
        long exceptImageId,
        CancellationToken cancellationToken)
    {
        var others = await dbContext.ProductImages
            .Where(image => image.ProductId == productId && image.Id != exceptImageId && image.IsPrimary)
            .ToListAsync(cancellationToken);
        foreach (var image in others)
            image.IsPrimary = false;
        if (others.Count > 0)
            await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static ProductImageReadModel ToReadModel(ProductImage image) =>
        new(
            image.Id,
            image.ProductId,
            image.OriginalName,
            image.MimeType,
            image.Size,
            image.Width,
            image.Height,
            image.ThumbPath,
            image.MediumPath,
            image.OriginalPath,
            image.IsPrimary,
            image.SortOrder,
            image.CreatedAt,
            image.UpdatedAt);
}
