using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using RetailPOS.API.DTOs.Product;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace RetailPOS.API.Services;

public interface IProductMediaService
{
    Task<List<ProductImageDto>> ListAsync(long productId);
    Task<ProductImageDto> UploadAsync(long productId, IFormFile file, bool isPrimary);
    Task DeleteAsync(long productId, long imageId);
    Task<long> SetPrimaryAsync(long productId, long imageId);
}

public sealed class ProductMediaService : IProductMediaService
{
    public const int MinDim = 800;
    public const int MaxDim = 1200;
    public const int MaxBytes = 5 * 1024 * 1024;
    public const int ThumbDim = 200;
    public const int MediumDim = 600;

    private static readonly HashSet<string> AllowedMime = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/jpg", "image/png", "image/webp"
    };

    private readonly RetailPOSDbContext _db;
    private readonly IWebHostEnvironment _env;

    public ProductMediaService(RetailPOSDbContext db, IWebHostEnvironment env)
    {
        _db = db;
        _env = env;
    }

    public async Task<List<ProductImageDto>> ListAsync(long productId)
    {
        var images = await _db.ProductImages
            .Where(i => i.ProductId == productId)
            .OrderByDescending(i => i.IsPrimary)
            .ThenBy(i => i.SortOrder)
            .ThenBy(i => i.Id)
            .ToListAsync();
        return images.Select(ToDto).ToList();
    }

    public async Task<ProductImageDto> UploadAsync(long productId, IFormFile file, bool isPrimary)
    {
        if (file is null || file.Length == 0)
            throw new ArgumentException("No file uploaded.");
        if (file.Length > MaxBytes)
            throw new ArgumentException($"File too large; limit is {MaxBytes / (1024 * 1024)} MB.");
        if (!AllowedMime.Contains(file.ContentType))
            throw new ArgumentException("Only JPG, PNG, and WEBP images are allowed.");

        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == productId)
            ?? throw new KeyNotFoundException($"Product {productId} not found.");

        await using var src = file.OpenReadStream();
        using var image = await Image.LoadAsync(src);

        if (image.Width < MinDim || image.Height < MinDim || image.Width > MaxDim || image.Height > MaxDim)
        {
            throw new ArgumentException(
                $"Image dimensions must be between {MinDim}x{MinDim} and {MaxDim}x{MaxDim}px. " +
                $"Got {image.Width}x{image.Height}.");
        }

        var hasExisting = await _db.ProductImages.AnyAsync(i => i.ProductId == productId);
        var willBePrimary = isPrimary || !hasExisting;

        var ext = ExtensionFromMime(file.ContentType);
        var sortOrder = await _db.ProductImages.Where(i => i.ProductId == productId)
            .Select(i => (int?)i.SortOrder).MaxAsync() ?? -1;

        var entity = new ProductImage
        {
            ProductId = productId,
            OriginalName = SanitizeName(file.FileName),
            MimeType = file.ContentType,
            Size = (int)file.Length,
            Width = image.Width,
            Height = image.Height,
            OriginalPath = "",
            MediumPath = "",
            ThumbPath = "",
            IsPrimary = false,
            SortOrder = sortOrder + 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        _db.ProductImages.Add(entity);
        await _db.SaveChangesAsync();

        var webRoot = ResolveWebRoot();
        var dir = Path.Combine(webRoot, "uploads", "products", productId.ToString());
        Directory.CreateDirectory(dir);

        var originalPath = Path.Combine(dir, $"{entity.Id}-original{ext}");
        var mediumPath = Path.Combine(dir, $"{entity.Id}-medium.webp");
        var thumbPath = Path.Combine(dir, $"{entity.Id}-thumb.webp");

        // Original — write the source bytes byte-for-byte
        await using (var fs = File.Create(originalPath))
        {
            src.Position = 0;
            await src.CopyToAsync(fs);
        }

        // Medium WEBP (max 600 px on longer side)
        using (var medium = image.Clone(ctx => ctx.Resize(new ResizeOptions
        {
            Mode = ResizeMode.Max,
            Size = new Size(MediumDim, MediumDim),
        })))
        {
            await medium.SaveAsync(mediumPath, new WebpEncoder { Quality = 80 });
        }

        // Thumbnail WEBP (max 200 px)
        using (var thumb = image.Clone(ctx => ctx.Resize(new ResizeOptions
        {
            Mode = ResizeMode.Max,
            Size = new Size(ThumbDim, ThumbDim),
        })))
        {
            await thumb.SaveAsync(thumbPath, new WebpEncoder { Quality = 70 });
        }

        entity.OriginalPath = $"/uploads/products/{productId}/{entity.Id}-original{ext}";
        entity.MediumPath = $"/uploads/products/{productId}/{entity.Id}-medium.webp";
        entity.ThumbPath = $"/uploads/products/{productId}/{entity.Id}-thumb.webp";

        if (willBePrimary)
        {
            await ClearOtherPrimariesAsync(productId, entity.Id);
            entity.IsPrimary = true;
        }

        await _db.SaveChangesAsync();
        return ToDto(entity);
    }

    public async Task DeleteAsync(long productId, long imageId)
    {
        var image = await _db.ProductImages
            .FirstOrDefaultAsync(i => i.Id == imageId && i.ProductId == productId)
            ?? throw new KeyNotFoundException($"Image {imageId} not found for product {productId}.");

        var wasPrimary = image.IsPrimary;
        _db.ProductImages.Remove(image);
        await _db.SaveChangesAsync();

        // Best-effort delete on disk
        var webRoot = ResolveWebRoot();
        TryDelete(Path.Combine(webRoot, image.OriginalPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));
        TryDelete(Path.Combine(webRoot, image.MediumPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));
        TryDelete(Path.Combine(webRoot, image.ThumbPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));

        if (wasPrimary)
        {
            var next = await _db.ProductImages
                .Where(i => i.ProductId == productId)
                .OrderBy(i => i.SortOrder).ThenBy(i => i.Id)
                .FirstOrDefaultAsync();
            if (next is not null)
            {
                next.IsPrimary = true;
                next.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }
        }
    }

    public async Task<long> SetPrimaryAsync(long productId, long imageId)
    {
        var target = await _db.ProductImages
            .FirstOrDefaultAsync(i => i.Id == imageId && i.ProductId == productId)
            ?? throw new KeyNotFoundException($"Image {imageId} not found for product {productId}.");

        if (target.IsPrimary) return target.Id;

        await ClearOtherPrimariesAsync(productId, imageId);
        target.IsPrimary = true;
        target.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return target.Id;
    }

    private async Task ClearOtherPrimariesAsync(long productId, long exceptImageId)
    {
        var others = await _db.ProductImages
            .Where(i => i.ProductId == productId && i.Id != exceptImageId && i.IsPrimary)
            .ToListAsync();
        foreach (var o in others) o.IsPrimary = false;
        if (others.Count > 0) await _db.SaveChangesAsync();
    }

    private string ResolveWebRoot()
    {
        if (!string.IsNullOrEmpty(_env.WebRootPath)) return _env.WebRootPath;
        // Fallback when wwwroot doesn't exist yet
        var wr = Path.Combine(_env.ContentRootPath, "wwwroot");
        Directory.CreateDirectory(wr);
        return wr;
    }

    private static string ExtensionFromMime(string mime) => mime.ToLowerInvariant() switch
    {
        "image/jpeg" or "image/jpg" => ".jpg",
        "image/png" => ".png",
        "image/webp" => ".webp",
        _ => ".bin",
    };

    private static string SanitizeName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "upload";
        var safe = Path.GetFileName(name);
        return safe.Length > 260 ? safe[^260..] : safe;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore */ }
    }

    private static ProductImageDto ToDto(ProductImage i) => new()
    {
        Id = i.Id,
        ProductId = i.ProductId,
        OriginalName = i.OriginalName,
        MimeType = i.MimeType,
        Size = i.Size,
        Width = i.Width,
        Height = i.Height,
        ThumbUrl = i.ThumbPath,
        MediumUrl = i.MediumPath,
        OriginalUrl = i.OriginalPath,
        IsPrimary = i.IsPrimary,
        SortOrder = i.SortOrder,
        CreatedAt = i.CreatedAt,
        UpdatedAt = i.UpdatedAt,
    };
}
