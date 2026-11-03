using RetailPOS.Infrastructure.Products;
using SixLabors.ImageSharp;

namespace RetailPOS.Tests.Products;

/// <summary>
/// Phase 2.1 Workstream H. SixLabors.ImageSharp 3.1.12 has open advisories whose fixed releases (3.2.0, 4.1.2) require a
/// Six Labors licence for Release builds, so the upload path is hardened instead: only JPEG, PNG and WebP decoders are
/// registered. A client can label any upload image/jpeg, so a malformed BigTIFF (GHSA-wmxv-xphr-5c9g) must be refused
/// as an unknown format before any TIFF code runs.
/// </summary>
public sealed class ProductImageProcessorSecurityTests
{
    private static byte[] MalformedBigTiff()
    {
        var bytes = new byte[24];
        bytes[0] = 0x49; bytes[1] = 0x49;
        bytes[2] = 0x2B; bytes[3] = 0x00;
        bytes[4] = 0x08; bytes[5] = 0x00;
        BitConverter.GetBytes((ulong)16).CopyTo(bytes, 8);
        BitConverter.GetBytes(5_000_000_000UL).CopyTo(bytes, 16);
        return bytes;
    }

    // A valid 1x1 Group 4 TIFF (the GHSA-jjfr-hcj7-qf5w reproduction input).
    private static byte[] Group4Tiff() => Convert.FromBase64String(
        "SUkqAAgAAAAJAAABAwABAAAAAQAAAAEBAwABAAAAAQAAAAIBAwABAAAAAQAAAAMBAwABAAAABAAAAAYBAwABAAAAAAAAABEBBAABAAAAegAAABUBAwABAAAAAQAAABYBBAABAAAAAQAAABcBBAABAAAABAAAAAAAAACACACA");

    private static byte[] Gif() => Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7");

    public static TheoryData<string> Payloads => new() { "bigtiff", "group4-tiff", "gif" };

    [Theory]
    [MemberData(nameof(Payloads))]
    public async Task FormatsOutsideJpegPngAndWebp_AreRejectedPromptlyAsUnknownBeforeDecoding(string payload)
    {
        var bytes = payload switch
        {
            "bigtiff" => MalformedBigTiff(),
            "group4-tiff" => Group4Tiff(),
            _ => Gif()
        };

        var decode = Task.Run(async () =>
        {
            await using var stream = new MemoryStream(bytes);
            await new ProductImageProcessor().ProcessAsync(stream, CancellationToken.None);
        });
        var finished = await Task.WhenAny(decode, Task.Delay(TimeSpan.FromSeconds(10)));

        Assert.Same(decode, finished);
        await Assert.ThrowsAsync<UnknownImageFormatException>(() => decode);
    }

    [Fact]
    public async Task APngIsStillProcessedIntoMediumAndThumbnailWebpVariants()
    {
        using var source = new Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(900, 900);
        await using var png = new MemoryStream();
        await source.SaveAsPngAsync(png);
        png.Position = 0;

        var result = await new ProductImageProcessor().ProcessAsync(png, CancellationToken.None);

        Assert.Equal(900, result.Width);
        Assert.Equal(900, result.Height);
        Assert.NotEmpty(result.MediumWebp);
        Assert.NotEmpty(result.ThumbWebp);
    }
}
