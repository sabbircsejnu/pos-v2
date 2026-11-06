using RetailPOS.Application.Products.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.Processing;

namespace RetailPOS.Infrastructure.Products;

public sealed class ProductImageProcessor : IProductImageProcessor
{
    // Decoding allocates by pixel count, not file size, so a small file can claim an enormous canvas. This limit is
    // checked from the header before any pixels are decoded. The frontend mirrors it in PRODUCT_IMAGE_RULES.maxPixels.
    public const long MaxPixels = 50_000_000;

    private const int MaxStoredDimension = 2000;
    private const int StoredQuality = 85;
    private const int OptimizeAboveBytes = 2 * 1024 * 1024;
    private const int MediumDimension = 1200;
    private const int MediumQuality = 80;
    private const int ThumbDimension = 200;
    private const int ThumbQuality = 70;
    private const string WebpMimeType = "image/webp";

    // Only the formats the upload endpoint accepts (JPEG, PNG, WebP) are registered. A client controls the declared MIME
    // type but ImageSharp sniffs the real format, so without this a TIFF/BigTIFF payload would reach the TIFF decoder
    // affected by GHSA-wmxv-xphr-5c9g; here it is rejected as an unknown format before any TIFF code runs.
    // MaxFrames stops an animated file multiplying the pixel limit by its frame count.
    private static readonly DecoderOptions UploadDecoderOptions = new()
    {
        MaxFrames = 1,
        Configuration = new Configuration(
            new JpegConfigurationModule(),
            new PngConfigurationModule(),
            new WebpConfigurationModule())
    };

    public async Task<ProcessedProductImage> ProcessAsync(
        Stream content,
        CancellationToken cancellationToken)
    {
        await using var original = new MemoryStream();
        await content.CopyToAsync(original, cancellationToken);
        var originalBytes = original.ToArray();

        try
        {
            await using var headerSource = new MemoryStream(originalBytes, writable: false);
            var info = await Image.IdentifyAsync(UploadDecoderOptions, headerSource, cancellationToken);
            if ((long)info.Width * info.Height > MaxPixels)
            {
                throw new ArgumentException(
                    $"Image is too large to process: the limit is {MaxPixels / 1_000_000} megapixels. " +
                    $"Got {info.Width}x{info.Height}.");
            }

            await using var imageSource = new MemoryStream(originalBytes, writable: false);
            using var image = await Image.LoadAsync(UploadDecoderOptions, imageSource, cancellationToken);
            var mimeType = image.Metadata.DecodedImageFormat!.DefaultMimeType;

            // Rotate phone photos upright first, then drop camera metadata (including any GPS position) from everything
            // this method encodes. The colour profile stays so colours do not shift.
            image.Mutate(context => context.AutoOrient());
            image.Metadata.ExifProfile = null;
            image.Metadata.IptcProfile = null;
            image.Metadata.XmpProfile = null;

            var medium = await CreateWebpAsync(image, MediumDimension, MediumQuality, cancellationToken);
            var thumb = await CreateWebpAsync(image, ThumbDimension, ThumbQuality, cancellationToken);

            var exceedsStoredDimension = Math.Max(image.Width, image.Height) > MaxStoredDimension;
            if (exceedsStoredDimension || originalBytes.Length > OptimizeAboveBytes)
            {
                var optimized = await CreateWebpAsync(image, MaxStoredDimension, StoredQuality, cancellationToken);
                if (exceedsStoredDimension || optimized.Bytes.Length < originalBytes.Length)
                {
                    return new ProcessedProductImage(
                        optimized.Bytes,
                        medium.Bytes,
                        thumb.Bytes,
                        optimized.Width,
                        optimized.Height,
                        WebpMimeType);
                }
            }

            return new ProcessedProductImage(
                originalBytes,
                medium.Bytes,
                thumb.Bytes,
                image.Width,
                image.Height,
                mimeType);
        }
        catch (Exception exception) when (
            exception is ImageFormatException or NotSupportedException or InvalidMemoryOperationException)
        {
            throw new ArgumentException(
                "The file is not a valid JPG, PNG, or WEBP image, or it is corrupted.",
                exception);
        }
    }

    private static async Task<(byte[] Bytes, int Width, int Height)> CreateWebpAsync(
        Image image,
        int maxDimension,
        int quality,
        CancellationToken cancellationToken)
    {
        // Lossy regardless of the source format, so a lossless WebP upload is compressed too.
        var encoder = new WebpEncoder { FileFormat = WebpFileFormatType.Lossy, Quality = quality };
        await using var output = new MemoryStream();

        // ResizeMode.Max fits within the box and keeps the aspect ratio, but it also enlarges, so smaller images skip it.
        if (Math.Max(image.Width, image.Height) <= maxDimension)
        {
            await image.SaveAsync(output, encoder, cancellationToken);
            return (output.ToArray(), image.Width, image.Height);
        }

        using var resized = image.Clone(context => context.Resize(new ResizeOptions
        {
            Mode = ResizeMode.Max,
            Size = new Size(maxDimension, maxDimension)
        }));
        await resized.SaveAsync(output, encoder, cancellationToken);
        return (output.ToArray(), resized.Width, resized.Height);
    }
}
