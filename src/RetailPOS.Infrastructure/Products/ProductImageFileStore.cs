using Microsoft.Extensions.Logging;
using RetailPOS.Application.Products.Abstractions;

namespace RetailPOS.Infrastructure.Products;

public sealed class ProductImageFileStore(
    ProductImageStorageOptions options,
    ILogger<ProductImageFileStore> logger) : IProductImageFileStore
{
    public async Task<ProductImageFilePaths> SaveAsync(
        long productId,
        long imageId,
        string extension,
        ProcessedProductImage image,
        CancellationToken cancellationToken)
    {
        var directory = Path.Combine(
            ResolveWebRoot(),
            "uploads",
            "products",
            productId.ToString());
        Directory.CreateDirectory(directory);

        var originalFile = $"{imageId}-original{extension}";
        var mediumFile = $"{imageId}-medium.webp";
        var thumbFile = $"{imageId}-thumb.webp";
        await File.WriteAllBytesAsync(Path.Combine(directory, originalFile), image.Original, cancellationToken);
        await File.WriteAllBytesAsync(Path.Combine(directory, mediumFile), image.MediumWebp, cancellationToken);
        await File.WriteAllBytesAsync(Path.Combine(directory, thumbFile), image.ThumbWebp, cancellationToken);

        var relativeDirectory = $"/uploads/products/{productId}/";
        return new ProductImageFilePaths(
            relativeDirectory + originalFile,
            relativeDirectory + mediumFile,
            relativeDirectory + thumbFile);
    }

    public Task TryDeleteAsync(ProductImageFilePaths paths, CancellationToken cancellationToken)
    {
        var webRoot = ResolveWebRoot();
        TryDelete(ResolvePath(webRoot, paths.OriginalPath), cancellationToken);
        TryDelete(ResolvePath(webRoot, paths.MediumPath), cancellationToken);
        TryDelete(ResolvePath(webRoot, paths.ThumbPath), cancellationToken);
        return Task.CompletedTask;
    }

    private string ResolveWebRoot()
    {
        if (!string.IsNullOrEmpty(options.WebRootPath))
            return options.WebRootPath;

        var webRoot = Path.Combine(options.ContentRootPath, "wwwroot");
        Directory.CreateDirectory(webRoot);
        return webRoot;
    }

    private static string ResolvePath(string webRoot, string url) =>
        Path.Combine(
            webRoot,
            url.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

    private void TryDelete(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException exception)
        {
            logger.LogWarning(exception, "Could not delete product image file {Path}", path);
        }
        catch (UnauthorizedAccessException exception)
        {
            logger.LogWarning(exception, "Could not delete product image file {Path}", path);
        }
    }
}
