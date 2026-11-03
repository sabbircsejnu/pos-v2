using RetailPOS.Application.Products.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace RetailPOS.Infrastructure.Products;

public sealed class ProductImageProcessor : IProductImageProcessor
{
    // Only the formats the upload endpoint accepts (JPEG, PNG, WebP) are registered. A client controls the declared MIME
    // type but ImageSharp sniffs the real format, so without this a TIFF/BigTIFF payload would reach the TIFF decoder
    // affected by GHSA-wmxv-xphr-5c9g; here it is rejected as an unknown format before any TIFF code runs.
    private static readonly DecoderOptions UploadDecoderOptions = new()
    {
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
        await using var imageSource = new MemoryStream(originalBytes, writable: false);
        using var image = await Image.LoadAsync(UploadDecoderOptions, imageSource, cancellationToken);

        var medium = await CreateWebpAsync(image, 600, 80, cancellationToken);
        var thumb = await CreateWebpAsync(image, 200, 70, cancellationToken);
        return new ProcessedProductImage(
            originalBytes,
            medium,
            thumb,
            image.Width,
            image.Height);
    }

    private static async Task<byte[]> CreateWebpAsync(
        Image image,
        int dimension,
        int quality,
        CancellationToken cancellationToken)
    {
        using var resized = image.Clone(context => context.Resize(new ResizeOptions
        {
            Mode = ResizeMode.Max,
            Size = new Size(dimension, dimension)
        }));

        await using var output = new MemoryStream();
        await resized.SaveAsync(output, new WebpEncoder { Quality = quality }, cancellationToken);
        return output.ToArray();
    }
}
