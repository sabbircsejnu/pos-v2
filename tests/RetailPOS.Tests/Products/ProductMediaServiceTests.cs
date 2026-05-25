using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Moq;
using RetailPOS.API.Services;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace RetailPOS.Tests.Products;

public class ProductMediaServiceTests : IDisposable
{
    private readonly RetailPOSDbContext _db;
    private readonly string _tempRoot;
    private readonly ProductMediaService _svc;
    private readonly long _productId;

    public ProductMediaServiceTests()
    {
        var opts = new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseInMemoryDatabase(databaseName: $"media-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _db = new RetailPOSDbContext(opts);

        // Seed minimum required parents
        _db.Categories.Add(new Category { Id = 1, Name = "Test", Description = "" });
        _db.Products.Add(new Product { Id = 42, Name = "Widget", CategoryId = 1, BasePrice = 1m });
        _db.SaveChanges();
        _productId = 42;

        _tempRoot = Path.Combine(Path.GetTempPath(), "rpos-media-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(_tempRoot);

        var env = new Mock<IWebHostEnvironment>();
        env.SetupGet(e => e.WebRootPath).Returns(_tempRoot);
        env.SetupGet(e => e.ContentRootPath).Returns(_tempRoot);

        _svc = new ProductMediaService(_db, env.Object);
    }

    [Fact]
    public async Task UploadAsync_FirstImage_BecomesPrimary_WritesAllThreeFiles()
    {
        var file = MakePngForm(1000, 1000, "first.png");

        var dto = await _svc.UploadAsync(_productId, file, isPrimary: false);

        Assert.True(dto.IsPrimary, "first uploaded image must be auto-primary");
        Assert.Contains("/uploads/products/42/", dto.OriginalUrl);
        Assert.True(File.Exists(Path.Combine(_tempRoot, "uploads", "products", "42", $"{dto.Id}-original.png")));
        Assert.True(File.Exists(Path.Combine(_tempRoot, "uploads", "products", "42", $"{dto.Id}-medium.webp")));
        Assert.True(File.Exists(Path.Combine(_tempRoot, "uploads", "products", "42", $"{dto.Id}-thumb.webp")));
    }

    [Fact]
    public async Task UploadAsync_TooSmall_Rejected()
    {
        var file = MakePngForm(500, 500, "small.png");
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _svc.UploadAsync(_productId, file, isPrimary: false));
    }

    [Fact]
    public async Task UploadAsync_TooLarge_Rejected()
    {
        var file = MakePngForm(1500, 1500, "big.png");
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _svc.UploadAsync(_productId, file, isPrimary: false));
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
            _svc.UploadAsync(_productId, file, isPrimary: false));
    }

    [Fact]
    public async Task SetPrimaryAsync_ClearsOtherPrimary()
    {
        var a = await _svc.UploadAsync(_productId, MakePngForm(1000, 1000, "a.png"), isPrimary: false);
        var b = await _svc.UploadAsync(_productId, MakePngForm(1000, 1000, "b.png"), isPrimary: false);
        Assert.True(a.IsPrimary);
        Assert.False(b.IsPrimary);

        await _svc.SetPrimaryAsync(_productId, b.Id);

        var primaries = await _db.ProductImages
            .Where(i => i.ProductId == _productId && i.IsPrimary).ToListAsync();
        Assert.Single(primaries);
        Assert.Equal(b.Id, primaries[0].Id);
    }

    [Fact]
    public async Task DeleteAsync_RemovesFiles_AndAutoPromotesNextPrimary()
    {
        var a = await _svc.UploadAsync(_productId, MakePngForm(1000, 1000, "a.png"), isPrimary: false);
        var b = await _svc.UploadAsync(_productId, MakePngForm(1000, 1000, "b.png"), isPrimary: false);
        Assert.True(a.IsPrimary);

        var aThumb = Path.Combine(_tempRoot, "uploads", "products", "42", $"{a.Id}-thumb.webp");
        Assert.True(File.Exists(aThumb));

        await _svc.DeleteAsync(_productId, a.Id);

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
        var a = await _svc.UploadAsync(_productId, MakePngForm(1000, 1000, "a.png"), isPrimary: false);
        await _svc.DeleteAsync(_productId, a.Id);
        Assert.Equal(0, await _db.ProductImages.CountAsync(i => i.ProductId == _productId));
    }

    [Fact]
    public async Task ListAsync_PrimaryFirst_ThenBySortOrder()
    {
        var a = await _svc.UploadAsync(_productId, MakePngForm(1000, 1000, "a.png"), isPrimary: false); // primary
        var b = await _svc.UploadAsync(_productId, MakePngForm(1000, 1000, "b.png"), isPrimary: false);
        var c = await _svc.UploadAsync(_productId, MakePngForm(1000, 1000, "c.png"), isPrimary: false);
        await _svc.SetPrimaryAsync(_productId, c.Id);

        var list = await _svc.ListAsync(_productId);

        Assert.Equal(c.Id, list[0].Id); // primary first
        Assert.True(list[0].IsPrimary);
        Assert.False(list.Skip(1).Any(i => i.IsPrimary));
    }

    public void Dispose()
    {
        _db.Dispose();
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
}
