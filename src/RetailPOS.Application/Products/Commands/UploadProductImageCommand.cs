using MediatR;
using RetailPOS.Application.Products.Abstractions;
using RetailPOS.Application.Products.ReadModels;

namespace RetailPOS.Application.Products.Commands;

public sealed record UploadProductImageCommand(
    long ProductId,
    Stream? Content,
    string? FileName,
    string? MimeType,
    long Length,
    bool IsPrimary) : IRequest<ProductImageReadModel>;

public sealed class UploadProductImageCommandHandler(
    IProductMediaStore store,
    IProductImageProcessor processor,
    IProductImageFileStore files) : IRequestHandler<UploadProductImageCommand, ProductImageReadModel>
{
    // Matches the thumbnail size, since images are never enlarged. Mirrored by PRODUCT_IMAGE_RULES.minDim in the frontend.
    public const int MinDimension = 200;
    public const int MaxBytes = 5 * 1024 * 1024;

    private static readonly HashSet<string> AllowedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/jpg",
        "image/png",
        "image/webp"
    };

    public async Task<ProductImageReadModel> Handle(
        UploadProductImageCommand request,
        CancellationToken cancellationToken)
    {
        if (request.Content is null || request.Length == 0)
            throw new ArgumentException("No file uploaded.");
        if (request.Length > MaxBytes)
            throw new ArgumentException($"File too large; limit is {MaxBytes / (1024 * 1024)} MB.");
        if (request.MimeType is null || !AllowedMimeTypes.Contains(request.MimeType))
            throw new ArgumentException("Only JPG, PNG, and WEBP images are allowed.");

        await store.EnsureProductExistsAsync(request.ProductId, cancellationToken);
        var image = await processor.ProcessAsync(request.Content, cancellationToken);
        if (image.Width < MinDimension || image.Height < MinDimension)
        {
            throw new ArgumentException(
                $"Image is too small: each side must be at least {MinDimension} px. " +
                $"Got {image.Width}x{image.Height}.");
        }

        // Metadata describes the file actually stored, which differs from the upload when it was optimized.
        var metadata = new ProductImageUploadMetadata(
            SanitizeName(request.FileName),
            image.OriginalMimeType,
            image.Original.Length,
            image.Width,
            image.Height,
            request.IsPrimary);
        var pending = await store.CreatePendingUploadAsync(request.ProductId, metadata, cancellationToken);
        var paths = await files.SaveAsync(
            request.ProductId,
            pending.Id,
            ExtensionFromMime(image.OriginalMimeType),
            image,
            cancellationToken);

        return await store.CompleteUploadAsync(
            request.ProductId,
            pending.Id,
            paths,
            pending.MakePrimary,
            cancellationToken);
    }

    private static string ExtensionFromMime(string mimeType) => mimeType.ToLowerInvariant() switch
    {
        "image/jpeg" or "image/jpg" => ".jpg",
        "image/png" => ".png",
        "image/webp" => ".webp",
        _ => ".bin"
    };

    private static string SanitizeName(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return "upload";

        var safe = Path.GetFileName(name);
        return safe.Length > 260 ? safe[^260..] : safe;
    }
}
