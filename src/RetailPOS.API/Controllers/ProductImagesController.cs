using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.Services;

namespace RetailPOS.API.Controllers;

[ApiController]
[Authorize(Policy = "products.view")]
[Route("api/products/{productId:long}/images")]
public class ProductImagesController : ControllerBase
{
    public sealed class UploadProductImageRequest
    {
        public IFormFile File { get; set; } = default!;
        public bool IsPrimary { get; set; }
    }

    private readonly IProductMediaService _media;
    private readonly ILogger<ProductImagesController> _logger;

    public ProductImagesController(IProductMediaService media, ILogger<ProductImagesController> logger)
    {
        _media = media;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> List(long productId)
    {
        var items = await _media.ListAsync(productId);
        return Ok(items);
    }

    [HttpPost]
    [Authorize(Policy = "products.edit")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<IActionResult> Upload(long productId, [FromForm] UploadProductImageRequest request)
    {
        try
        {
            var dto = await _media.UploadAsync(productId, request.File, request.IsPrimary);
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Image upload failed");
            return StatusCode(500, new { message = "Image upload failed" });
        }
    }

    [HttpDelete("{imageId:long}")]
    [Authorize(Policy = "products.edit")]
    public async Task<IActionResult> Delete(long productId, long imageId)
    {
        try
        {
            await _media.DeleteAsync(productId, imageId);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpPut("{imageId:long}/primary")]
    [Authorize(Policy = "products.edit")]
    public async Task<IActionResult> SetPrimary(long productId, long imageId)
    {
        try
        {
            var id = await _media.SetPrimaryAsync(productId, imageId);
            return Ok(new { primaryImageId = id });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }
}
