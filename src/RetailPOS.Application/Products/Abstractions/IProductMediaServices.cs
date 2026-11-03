using RetailPOS.Application.Products.ReadModels;

namespace RetailPOS.Application.Products.Abstractions;

public interface IProductMediaStore
{
    Task<IReadOnlyList<ProductImageReadModel>> ListAsync(long productId, CancellationToken cancellationToken);

    Task EnsureProductExistsAsync(long productId, CancellationToken cancellationToken);

    Task<PendingProductImageUpload> CreatePendingUploadAsync(
        long productId,
        ProductImageUploadMetadata metadata,
        CancellationToken cancellationToken);

    Task<ProductImageReadModel> CompleteUploadAsync(
        long productId,
        long imageId,
        ProductImageFilePaths paths,
        bool makePrimary,
        CancellationToken cancellationToken);

    Task<ProductImageDeletion> DeleteAsync(
        long productId,
        long imageId,
        CancellationToken cancellationToken);

    Task PromoteNextPrimaryAsync(long productId, CancellationToken cancellationToken);

    Task<long> SetPrimaryAsync(long productId, long imageId, CancellationToken cancellationToken);
}

public interface IProductImageProcessor
{
    Task<ProcessedProductImage> ProcessAsync(Stream content, CancellationToken cancellationToken);
}

public interface IProductImageFileStore
{
    Task<ProductImageFilePaths> SaveAsync(
        long productId,
        long imageId,
        string extension,
        ProcessedProductImage image,
        CancellationToken cancellationToken);

    Task TryDeleteAsync(ProductImageFilePaths paths, CancellationToken cancellationToken);
}

public sealed record ProductImageUploadMetadata(
    string OriginalName,
    string MimeType,
    int Size,
    int Width,
    int Height,
    bool IsPrimary);

public sealed record PendingProductImageUpload(long Id, bool MakePrimary);

public sealed record ProductImageFilePaths(string OriginalPath, string MediumPath, string ThumbPath);

public sealed record ProductImageDeletion(
    string OriginalPath,
    string MediumPath,
    string ThumbPath,
    bool WasPrimary);

public sealed record ProcessedProductImage(
    byte[] Original,
    byte[] MediumWebp,
    byte[] ThumbWebp,
    int Width,
    int Height);
