using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.Authorization;
using MediatR;
using RetailPOS.Application.Products.Commands;
using RetailPOS.Application.Products.Queries;

namespace RetailPOS.API.Controllers;

[ApiController]
// Authenticated by default; each action declares its policy so editing a product implies reading that record.
[Authorize]
[Route("api/products/{productId:long}/images")]
public class ProductImagesController : ControllerBase
{
    public sealed class UploadProductImageRequest
    {
        public IFormFile File { get; set; } = default!;
        public bool IsPrimary { get; set; }
    }

    private readonly ISender _sender;
    private readonly ILogger<ProductImagesController> _logger;

    public ProductImagesController(ISender sender, ILogger<ProductImagesController> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    [HttpGet]
    [Authorize(Policy = LookupAuthorizationPolicies.ProductRecordRead)]
    public async Task<IActionResult> List(long productId, CancellationToken cancellationToken)
    {
        var images = await _sender.Send(new GetProductImagesQuery(productId), cancellationToken);
        return Ok(images.Select(ToDto).ToList());
    }

    [HttpPost]
    [Authorize(Policy = "products.edit")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<IActionResult> Upload(
        long productId,
        [FromForm] UploadProductImageRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            using var content = request.File?.OpenReadStream();
            var image = await _sender.Send(new UploadProductImageCommand(
                productId,
                content,
                request.File?.FileName,
                request.File?.ContentType,
                request.File?.Length ?? 0,
                request.IsPrimary), cancellationToken);
            var dto = ToDto(image);
            return CreatedAtAction(nameof(List), new { productId }, dto);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Image upload failed");
            return StatusCode(500, new { message = "Image upload failed" });
        }
    }

    [HttpDelete("{imageId:long}")]
    [Authorize(Policy = "products.edit")]
    public async Task<IActionResult> Delete(long productId, long imageId, CancellationToken cancellationToken)
    {
        try
        {
            await _sender.Send(new DeleteProductImageCommand(productId, imageId), cancellationToken);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpPut("{imageId:long}/primary")]
    [Authorize(Policy = "products.edit")]
    public async Task<IActionResult> SetPrimary(long productId, long imageId, CancellationToken cancellationToken)
    {
        try
        {
            var id = await _sender.Send(new SetPrimaryProductImageCommand(productId, imageId), cancellationToken);
            return Ok(new { primaryImageId = id });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    private static RetailPOS.API.DTOs.Product.ProductImageDto ToDto(
        RetailPOS.Application.Products.ReadModels.ProductImageReadModel image) =>
        new()
        {
            Id = image.Id,
            ProductId = image.ProductId,
            OriginalName = image.OriginalName,
            MimeType = image.MimeType,
            Size = image.Size,
            Width = image.Width,
            Height = image.Height,
            ThumbUrl = image.ThumbUrl,
            MediumUrl = image.MediumUrl,
            OriginalUrl = image.OriginalUrl,
            IsPrimary = image.IsPrimary,
            SortOrder = image.SortOrder,
            CreatedAt = image.CreatedAt,
            UpdatedAt = image.UpdatedAt
        };
}
