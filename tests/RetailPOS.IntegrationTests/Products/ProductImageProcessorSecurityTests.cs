using RetailPOS.Infrastructure.Products;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;

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
        // Reported as a validation error (HTTP 400), but still refused by format sniffing before any decoder runs.
        var exception = await Assert.ThrowsAsync<ArgumentException>(() => decode);
        Assert.IsType<UnknownImageFormatException>(exception.InnerException);
    }

    [Fact]
    public async Task ADecompressionBombIsRejectedFromItsHeaderBeforeThePixelsAreDecoded()
    {
        var decode = Task.Run(async () =>
        {
            await using var stream = new MemoryStream(PngHeaderDeclaring(30_000, 30_000));
            await new ProductImageProcessor().ProcessAsync(stream, CancellationToken.None);
        });
        var finished = await Task.WhenAny(decode, Task.Delay(TimeSpan.FromSeconds(10)));

        Assert.Same(decode, finished);
        var exception = await Assert.ThrowsAsync<ArgumentException>(() => decode);
        Assert.Equal("Image is too large to process: the limit is 50 megapixels. Got 30000x30000.", exception.Message);
    }

    [Fact]
    public async Task ATruncatedImageIsRejectedAsCorrupted()
    {
        using var source = new Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(900, 900);
        await using var png = new MemoryStream();
        await source.SaveAsPngAsync(png);
        var truncated = png.ToArray()[..40];

        await using var stream = new MemoryStream(truncated);
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            new ProductImageProcessor().ProcessAsync(stream, CancellationToken.None));

        Assert.Equal("The file is not a valid JPG, PNG, or WEBP image, or it is corrupted.", exception.Message);
    }

    [Theory]
    [InlineData("png")]
    [InlineData("webp")]
    public async Task AFileCutOffHalfwayThroughItsPixelDataIsRejectedAsCorrupted(string format)
    {
        using var source = new Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(900, 900);
        var noise = new Random(7);
        source.ProcessPixelRows(rows =>
        {
            for (var y = 0; y < rows.Height; y++)
            {
                noise.NextBytes(System.Runtime.InteropServices.MemoryMarshal.AsBytes(rows.GetRowSpan(y)));
            }
        });
        await using var encoded = new MemoryStream();
        if (format == "png")
        {
            await source.SaveAsPngAsync(encoded);
        }
        else
        {
            await source.SaveAsWebpAsync(encoded);
        }
        var whole = encoded.ToArray();

        await using var stream = new MemoryStream(whole[..(whole.Length / 2)]);
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            new ProductImageProcessor().ProcessAsync(stream, CancellationToken.None));

        Assert.Equal("The file is not a valid JPG, PNG, or WEBP image, or it is corrupted.", exception.Message);
    }

    [Fact]
    public async Task ARotatedPhotoIsUprightInEveryVariantAndCarriesNoExifMetadata()
    {
        using var source = new Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(1600, 1200);
        source.Metadata.ExifProfile = new ExifProfile();
        source.Metadata.ExifProfile.SetValue(ExifTag.Orientation, (ushort)6);
        await using var jpeg = new MemoryStream();
        await source.SaveAsJpegAsync(jpeg);
        jpeg.Position = 0;

        var result = await new ProductImageProcessor().ProcessAsync(jpeg, CancellationToken.None);

        Assert.Equal((1200, 1600), (result.Width, result.Height));
        using var medium = Image.Load(result.MediumWebp);
        Assert.Equal((900, 1200), (medium.Width, medium.Height));
        Assert.Null(medium.Metadata.ExifProfile);
        using var thumb = Image.Load(result.ThumbWebp);
        Assert.Equal((150, 200), (thumb.Width, thumb.Height));
        Assert.Null(thumb.Metadata.ExifProfile);
    }

    [Fact]
    public async Task AHeavyFileWithinTheDimensionCapIsCompressedWithoutResizing()
    {
        using var source = new Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(1000, 1000);
        var noise = new Random(42);
        source.ProcessPixelRows(rows =>
        {
            for (var y = 0; y < rows.Height; y++)
            {
                noise.NextBytes(System.Runtime.InteropServices.MemoryMarshal.AsBytes(rows.GetRowSpan(y)));
            }
        });
        await using var png = new MemoryStream();
        await source.SaveAsPngAsync(png);
        var uploaded = png.ToArray();
        Assert.InRange(uploaded.Length, 2 * 1024 * 1024 + 1, 5 * 1024 * 1024);
        await using var stream = new MemoryStream(uploaded);

        var result = await new ProductImageProcessor().ProcessAsync(stream, CancellationToken.None);

        Assert.Equal("image/webp", result.OriginalMimeType);
        Assert.True(result.Original.Length < uploaded.Length, "the stored copy must be smaller than the upload");
        Assert.Equal((1000, 1000), (result.Width, result.Height));
    }

    /// <summary>A structurally valid PNG whose header claims the given size but carries no pixel data.</summary>
    private static byte[] PngHeaderDeclaring(int width, int height)
    {
        var header = new byte[13];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header, width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; // bit depth
        header[9] = 6; // RGBA

        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        WriteChunk(png, "IHDR", header);
        WriteChunk(png, "IDAT", []);
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void WriteChunk(Stream target, string type, byte[] data)
    {
        Span<byte> number = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
        target.Write(number);
        byte[] typed = [.. System.Text.Encoding.ASCII.GetBytes(type), .. data];
        target.Write(typed);
        var crc = 0xFFFFFFFFu;
        foreach (var value in typed)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(number, ~crc);
        target.Write(number);
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
