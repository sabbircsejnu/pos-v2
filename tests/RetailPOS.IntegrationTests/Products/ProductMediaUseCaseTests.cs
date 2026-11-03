using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using RetailPOS.Application.Products.Commands;
using RetailPOS.Application.Products.Queries;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Products;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace RetailPOS.Tests.Products;

public class ProductMediaUseCaseTests : IDisposable
{
    private readonly RetailPOSDbContext _db;
    private readonly SqliteConnection _connection;
    private readonly string _tempRoot;
    private readonly GetProductImagesQueryHandler _list;
    private readonly UploadProductImageCommandHandler _upload;
    private readonly DeleteProductImageCommandHandler _delete;
    private readonly SetPrimaryProductImageCommandHandler _setPrimary;
    private readonly long _productId;

    public ProductMediaUseCaseTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var opts = new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new RetailPOSDbContext(opts);
        _db.Database.EnsureCreated();

        // Seed minimum required parents
        _db.Businesses.Add(new Business { Id = 1, Name = "Test Business 1" });
        _db.Categories.Add(new Category { Id = 1, BusinessId = 1, Name = "Test", Description = "" });
        _db.Products.Add(new Product { Id = 42, BusinessId = 1, Name = "Widget", CategoryId = 1, BasePrice = 1m });
        _db.SaveChanges();
        _productId = 42;

        _tempRoot = Path.Combine(Path.GetTempPath(), "rpos-media-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(_tempRoot);

        var store = new ProductMediaStore(_db);
        var files = new ProductImageFileStore(
            new ProductImageStorageOptions(_tempRoot, _tempRoot),
            NullLogger<ProductImageFileStore>.Instance);
        _list = new GetProductImagesQueryHandler(store);
        _upload = new UploadProductImageCommandHandler(store, new ProductImageProcessor(), files);
        _delete = new DeleteProductImageCommandHandler(store, files);
        _setPrimary = new SetPrimaryProductImageCommandHandler(store);
    }

    [Fact]
    public async Task UploadAsync_FirstImage_BecomesPrimary_WritesAllThreeFiles()
    {
        var file = MakePngForm(1000, 1000, "first.png");

        var dto = await UploadAsync(file, isPrimary: false);

        Assert.True(dto.IsPrimary, "first uploaded image must be auto-primary");
        Assert.Contains("/uploads/products/42/", dto.OriginalUrl);
        var originalPath = Path.Combine(_tempRoot, "uploads", "products", "42", $"{dto.Id}-original.png");
        Assert.True(File.Exists(originalPath));
        using var source = file.OpenReadStream();
        using var expectedOriginal = new MemoryStream();
        await source.CopyToAsync(expectedOriginal);
        Assert.Equal(expectedOriginal.ToArray(), await File.ReadAllBytesAsync(originalPath));
        Assert.True(File.Exists(Path.Combine(_tempRoot, "uploads", "products", "42", $"{dto.Id}-medium.webp")));
        Assert.True(File.Exists(Path.Combine(_tempRoot, "uploads", "products", "42", $"{dto.Id}-thumb.webp")));
    }

    [Fact]
    public async Task UploadAsync_TooSmall_Rejected()
    {
        var file = MakePngForm(500, 500, "small.png");
        await Assert.ThrowsAsync<ArgumentException>(() =>
            UploadAsync(file, isPrimary: false));
    }

    [Fact]
    public async Task UploadAsync_TooLarge_Rejected()
    {
        var file = MakePngForm(1500, 1500, "big.png");
        await Assert.ThrowsAsync<ArgumentException>(() =>
            UploadAsync(file, isPrimary: false));
    }

    [Fact]
    public async Task UploadAsync_ExceedsByteLimit_RejectedBeforeImageDecoding()
    {
        var content = new MemoryStream(new byte[UploadProductImageCommandHandler.MaxBytes + 1]);
        var file = new FormFile(content, 0, content.Length, "file", "oversized.png")
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/png"
        };

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            UploadAsync(file, isPrimary: false));

        Assert.Equal("File too large; limit is 5 MB.", exception.Message);
        Assert.Empty(await _db.ProductImages.ToListAsync());
    }

    [Fact]
    public async Task UploadAsync_DisallowedMime_Rejected()
    {
        var bytes = Encoding.UTF8.GetBytes("not an image");
        var ms = new MemoryStream(bytes);
        var file = new FormFile(ms, 0, bytes.Length, "file", "evil.bmp")
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/bmp",
        };
        await Assert.ThrowsAsync<ArgumentException>(() =>
            UploadAsync(file, isPrimary: false));
    }

    [Fact]
    public async Task UploadAsync_MissingProduct_IsRejectedBeforeImageDecoding()
    {
        var invalidImage = new FormFile(
            new MemoryStream(Encoding.UTF8.GetBytes("not an image")),
            0,
            12,
            "file",
            "missing.png")
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/png"
        };

        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            UploadAsync(invalidImage, isPrimary: false, productId: 404));

        Assert.Equal("Product 404 not found.", exception.Message);
        Assert.Empty(await _db.ProductImages.ToListAsync());
    }

    [Fact]
    public async Task SetPrimaryAsync_ClearsOtherPrimary()
    {
        var a = await UploadAsync(MakePngForm(1000, 1000, "a.png"), isPrimary: false);
        var b = await UploadAsync(MakePngForm(1000, 1000, "b.png"), isPrimary: false);
        Assert.True(a.IsPrimary);
        Assert.False(b.IsPrimary);

        await _setPrimary.Handle(new SetPrimaryProductImageCommand(_productId, b.Id), CancellationToken.None);

        var primaries = await _db.ProductImages
            .Where(i => i.ProductId == _productId && i.IsPrimary).ToListAsync();
        Assert.Single(primaries);
        Assert.Equal(b.Id, primaries[0].Id);
    }

    [Fact]
    public async Task DeleteAsync_RemovesFiles_AndAutoPromotesNextPrimary()
    {
        var a = await UploadAsync(MakePngForm(1000, 1000, "a.png"), isPrimary: false);
        var b = await UploadAsync(MakePngForm(1000, 1000, "b.png"), isPrimary: false);
        Assert.True(a.IsPrimary);

        var aThumb = Path.Combine(_tempRoot, "uploads", "products", "42", $"{a.Id}-thumb.webp");
        Assert.True(File.Exists(aThumb));

        await _delete.Handle(new DeleteProductImageCommand(_productId, a.Id), CancellationToken.None);

        Assert.False(File.Exists(aThumb), "thumb file should be gone after delete");
        var remaining = await _db.ProductImages
            .Where(i => i.ProductId == _productId).ToListAsync();
        Assert.Single(remaining);
        Assert.Equal(b.Id, remaining[0].Id);
        Assert.True(remaining[0].IsPrimary, "next image should auto-promote to primary");
    }

    [Fact]
    public async Task DeleteAsync_LastImage_LeavesNoPrimary()
    {
        var a = await UploadAsync(MakePngForm(1000, 1000, "a.png"), isPrimary: false);
        await _delete.Handle(new DeleteProductImageCommand(_productId, a.Id), CancellationToken.None);
        Assert.Equal(0, await _db.ProductImages.CountAsync(i => i.ProductId == _productId));
    }

    [Fact]
    public async Task ListAsync_PrimaryFirst_ThenBySortOrder()
    {
        var a = await UploadAsync(MakePngForm(1000, 1000, "a.png"), isPrimary: false); // primary
        var b = await UploadAsync(MakePngForm(1000, 1000, "b.png"), isPrimary: false);
        var c = await UploadAsync(MakePngForm(1000, 1000, "c.png"), isPrimary: false);
        await _setPrimary.Handle(new SetPrimaryProductImageCommand(_productId, c.Id), CancellationToken.None);

        var list = await _list.Handle(new GetProductImagesQuery(_productId), CancellationToken.None);

        Assert.Equal(c.Id, list[0].Id); // primary first
        Assert.True(list[0].IsPrimary);
        Assert.DoesNotContain(list.Skip(1), i => i.IsPrimary);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        try { Directory.Delete(_tempRoot, recursive: true); } catch { /* best-effort */ }
    }

    private static FormFile MakePngForm(int w, int h, string name)
    {
        var ms = new MemoryStream();
        using (var img = new Image<Rgba32>(w, h))
        {
            img.SaveAsPng(ms, new PngEncoder());
        }
        ms.Position = 0;
        return new FormFile(ms, 0, ms.Length, "file", name)
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/png",
        };
    }

    private async Task<RetailPOS.Application.Products.ReadModels.ProductImageReadModel> UploadAsync(
        FormFile file,
        bool isPrimary,
        long? productId = null)
    {
        await using var content = file.OpenReadStream();
        return await _upload.Handle(
            new UploadProductImageCommand(
                productId ?? _productId,
                content,
                file.FileName,
                file.ContentType,
                file.Length,
                isPrimary),
            CancellationToken.None);
    }
}
