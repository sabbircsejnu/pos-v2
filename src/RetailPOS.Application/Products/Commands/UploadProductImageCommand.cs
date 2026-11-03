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
    public const int MinDimension = 800;
    public const int MaxDimension = 1200;
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
        if (image.Width < MinDimension
            || image.Height < MinDimension
            || image.Width > MaxDimension
            || image.Height > MaxDimension)
        {
            throw new ArgumentException(
                $"Image dimensions must be between {MinDimension}x{MinDimension} and {MaxDimension}x{MaxDimension}px. " +
                $"Got {image.Width}x{image.Height}.");
        }

        var metadata = new ProductImageUploadMetadata(
            SanitizeName(request.FileName),
            request.MimeType,
            checked((int)request.Length),
            image.Width,
            image.Height,
            request.IsPrimary);
        var pending = await store.CreatePendingUploadAsync(request.ProductId, metadata, cancellationToken);
        var paths = await files.SaveAsync(
            request.ProductId,
            pending.Id,
            ExtensionFromMime(request.MimeType),
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
