using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using MediatR;
using Moq;
using RetailPOS.API.Controllers;
using RetailPOS.API.DTOs.Product;
using RetailPOS.Application.Products.ReadModels;

namespace RetailPOS.Tests.Products;

public class ProductImagesControllerCharacterizationTests
{
    [Fact]
    public async Task List_ReturnsServiceProjection_AndKeepsViewRoutePolicy()
    {
        var images = new List<ProductImageReadModel>
        {
            new(7, 42, "", "image/png", 1, 800, 800, "", "", "", true, 0, DateTime.UtcNow, DateTime.UtcNow)
        };
        var sender = new Mock<ISender>();
        sender.Setup(service => service.Send(
                It.IsAny<IRequest<IReadOnlyList<ProductImageReadModel>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ProductImageReadModel>)images);
        var controller = new ProductImagesController(sender.Object, NullLogger<ProductImagesController>.Instance);

        var result = await controller.List(42, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<List<ProductImageDto>>(ok.Value);
        Assert.Equal(7, response.Single().Id);
        var route = Assert.Single(typeof(ProductImagesController).GetCustomAttributes(typeof(RouteAttribute), true)
            .Cast<RouteAttribute>());
        Assert.Equal("api/products/{productId:long}/images", route.Template);
        Assert.Equal("products.view", Assert.Single(typeof(ProductImagesController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>()).Policy);
        sender.Verify(service => service.Send(
            It.IsAny<IRequest<IReadOnlyList<ProductImageReadModel>>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Upload_ReturnsCreatedAtList_WithProductRouteValue()
    {
        var uploaded = new ProductImageDto { Id = 9, ProductId = 42, OriginalUrl = "/uploads/products/42/9-original.png" };
        var sender = new Mock<ISender>();
        sender.Setup(service => service.Send(
                It.IsAny<IRequest<ProductImageReadModel>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductImageReadModel(
                uploaded.Id,
                uploaded.ProductId,
                uploaded.OriginalName,
                uploaded.MimeType,
                uploaded.Size,
                uploaded.Width,
                uploaded.Height,
                uploaded.ThumbUrl,
                uploaded.MediumUrl,
                uploaded.OriginalUrl,
                uploaded.IsPrimary,
                uploaded.SortOrder,
                uploaded.CreatedAt,
                uploaded.UpdatedAt));
        var controller = new ProductImagesController(sender.Object, NullLogger<ProductImagesController>.Instance);

        var result = await controller.Upload(42, new ProductImagesController.UploadProductImageRequest
        {
            File = MakeFile("image.png"),
            IsPrimary = true
        }, CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(nameof(ProductImagesController.List), created.ActionName);
        Assert.Equal(42L, created.RouteValues!["productId"]);
        Assert.Equal(uploaded.Id, Assert.IsType<ProductImageDto>(created.Value).Id);
        var upload = typeof(ProductImagesController).GetMethod(nameof(ProductImagesController.Upload))!;
        Assert.Equal("products.edit", Assert.Single(upload.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>()).Policy);
        Assert.Equal("multipart/form-data", Assert.Single(upload.GetCustomAttributes(typeof(ConsumesAttribute), true)
            .Cast<ConsumesAttribute>().Single().ContentTypes));
        var requestSizeLimit = Assert.Single(
            upload.GetCustomAttributesData(),
            attribute => attribute.AttributeType == typeof(RequestSizeLimitAttribute));
        Assert.Equal(6 * 1024 * 1024, Convert.ToInt64(requestSizeLimit.ConstructorArguments.Single().Value));
    }

    [Fact]
    public async Task Upload_MapsValidationAndMissingProductErrorsToExistingBodies()
    {
        var sender = new Mock<ISender>();
        sender.Setup(service => service.Send(
                It.IsAny<IRequest<ProductImageReadModel>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("Only JPG, PNG, and WEBP images are allowed."));
        var controller = new ProductImagesController(sender.Object, NullLogger<ProductImagesController>.Instance);
        var request = new ProductImagesController.UploadProductImageRequest
        {
            File = MakeFile("image.bmp")
        };

        var badRequest = Assert.IsType<BadRequestObjectResult>(
            await controller.Upload(42, request, CancellationToken.None));
        Assert.Equal(
            "Only JPG, PNG, and WEBP images are allowed.",
            badRequest.Value!.GetType().GetProperty("message")!.GetValue(badRequest.Value));

        sender.Setup(service => service.Send(
                It.IsAny<IRequest<ProductImageReadModel>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException("Product 42 not found."));
        var notFound = Assert.IsType<NotFoundObjectResult>(
            await controller.Upload(42, request, CancellationToken.None));
        Assert.Equal(
            "Product 42 not found.",
            notFound.Value!.GetType().GetProperty("message")!.GetValue(notFound.Value));
    }

    [Fact]
    public async Task DeleteAndSetPrimary_PreserveSuccessAndNotFoundResponses()
    {
        var sender = new Mock<ISender>();
        sender.Setup(service => service.Send(
                It.IsAny<IRequest<long>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(9);
        sender.Setup(service => service.Send(It.IsAny<IRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var controller = new ProductImagesController(sender.Object, NullLogger<ProductImagesController>.Instance);

        Assert.IsType<NoContentResult>(await controller.Delete(42, 9, CancellationToken.None));
        var primary = Assert.IsType<OkObjectResult>(
            await controller.SetPrimary(42, 9, CancellationToken.None));
        Assert.Equal(9L, primary.Value!.GetType().GetProperty("primaryImageId")!.GetValue(primary.Value));

        sender.Setup(service => service.Send(It.IsAny<IRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException("Image 10 not found for product 42."));
        var notFound = Assert.IsType<NotFoundObjectResult>(
            await controller.Delete(42, 10, CancellationToken.None));
        Assert.Equal(
            "Image 10 not found for product 42.",
            notFound.Value!.GetType().GetProperty("message")!.GetValue(notFound.Value));
    }

    private static FormFile MakeFile(string fileName)
    {
        var content = new MemoryStream([1]);
        return new FormFile(content, 0, content.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/png"
        };
    }
}
