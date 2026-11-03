using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MediatR;
using Moq;
using RetailPOS.API.Controllers;
using RetailPOS.API.DTOs.Sale;
using RetailPOS.API.Models;
using RetailPOS.API.Services;
using RetailPOS.Application.Sales.Commands;
using RetailPOS.Application.Sales.Queries;
using RetailPOS.Application.Sales.ReadModels;

namespace RetailPOS.Tests.Sales;

public class SalesControllerActorTests
{
    [Fact]
    public async Task VoidAndRefund_RejectRequestsWithoutPositiveAuthenticatedUserId()
    {
        var sender = new Mock<ISender>();
        var users = new[]
        {
            new ClaimsPrincipal(new ClaimsIdentity()),
            new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "0")])),
            new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "invalid")]))
        };

        foreach (var user in users)
        {
            var controller = CreateController(user, sender.Object);
            var voidResult = await controller.Void(77, new VoidSaleDto { Reason = "reason" }, CancellationToken.None);
            var refundResult = await controller.Refund(
                77,
                new RefundSaleDto { Reason = "reason" },
                CancellationToken.None);
            Assert.IsType<UnauthorizedObjectResult>(voidResult.Result);
            Assert.IsType<UnauthorizedObjectResult>(refundResult.Result);
        }

        sender.Verify(x => x.Send(
            It.IsAny<IRequest<SaleReadModel>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Refund_AuthorizesSaleOutletBeforeDispatchingApplicationCommand()
    {
        var outletAccess = new Mock<IUserOutletAccessService>();
        outletAccess.Setup(service => service.ResolveAndAuthorizeLocationAsync(5001, "outlet"))
            .ReturnsAsync(((long?)5001, "outlet"));
        var featureEntitlement = new Mock<IFeatureEntitlementService>();
        featureEntitlement.Setup(service => service.EnsureFeatureEnabledAsync("sales.refund", null))
            .Returns(Task.CompletedTask);
        var sale = CreateSale("completed");
        var dispatched = new List<IRequest<SaleReadModel>>();
        var sender = new Mock<ISender>();
        sender.Setup(service => service.Send(
                It.IsAny<IRequest<SaleReadModel>>(),
                It.IsAny<CancellationToken>()))
            .Returns<IRequest<SaleReadModel>, CancellationToken>((request, _) =>
            {
                dispatched.Add(request);
                return Task.FromResult(request is RefundSaleCommand ? sale with { Status = "refunded" } : sale);
            });
        var controller = CreateController(
            new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "5")],
                "test")),
            sender.Object,
            outletAccess.Object,
            featureEntitlement.Object);
        var request = new RefundSaleDto
        {
            Items = [new RefundItemDto { VariantId = 3201, Quantity = 1 }],
            Reason = "customer return"
        };

        var result = await controller.Refund(77, request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ApiResponse<SaleDto>>(ok.Value);
        Assert.True(response.Success);
        Assert.Equal("Sale refunded successfully", response.Message);
        Assert.Equal("refunded", response.Data?.Status);
        Assert.Equal("S-77", response.Data?.SaleNumber);
        Assert.Collection(
            dispatched,
            item => Assert.IsType<GetSaleByIdQuery>(item),
            item =>
            {
                var command = Assert.IsType<RefundSaleCommand>(item);
                Assert.Equal(77, command.SaleId);
                Assert.Equal(5, command.ActorUserId);
                Assert.Equal("customer return", command.Reason);
                Assert.Equal([new RefundSaleLine(3201, 1)], command.Items);
            });
        outletAccess.Verify(
            service => service.ResolveAndAuthorizeLocationAsync(5001, "outlet"),
            Times.Once);
    }

    [Fact]
    public async Task Refund_DoesNotDispatchCommandWhenSaleOutletIsDenied()
    {
        var outletAccess = new Mock<IUserOutletAccessService>();
        outletAccess.Setup(service => service.ResolveAndAuthorizeLocationAsync(5001, "outlet"))
            .ThrowsAsync(new UnauthorizedAccessException("Outlet denied"));
        var featureEntitlement = new Mock<IFeatureEntitlementService>();
        featureEntitlement.Setup(service => service.EnsureFeatureEnabledAsync("sales.refund", null))
            .Returns(Task.CompletedTask);
        var sender = new Mock<ISender>();
        sender.Setup(service => service.Send(
                It.IsAny<IRequest<SaleReadModel>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSale("completed"));
        var controller = CreateController(
            new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "5")],
                "test")),
            sender.Object,
            outletAccess.Object,
            featureEntitlement.Object);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            controller.Refund(
                77,
                new RefundSaleDto { Items = [new RefundItemDto { VariantId = 3201, Quantity = 1 }] },
                CancellationToken.None));

        sender.Verify(
            service => service.Send(
                It.IsAny<IRequest<SaleReadModel>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        sender.Verify(
            service => service.Send(
                It.Is<IRequest<SaleReadModel>>(request => request is RefundSaleCommand),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Void_AuthorizesSaleOutletBeforeDispatchingApplicationCommand()
    {
        var outletAccess = new Mock<IUserOutletAccessService>();
        outletAccess.Setup(service => service.ResolveAndAuthorizeLocationAsync(5001, "outlet"))
            .ReturnsAsync(((long?)5001, "outlet"));
        var sender = new Mock<ISender>();
        var sale = new SaleReadModel(
            77,
            "S-77",
            5001,
            "Outlet",
            "Address",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            5,
            "Cashier",
            DateTime.UtcNow,
            120m,
            0m,
            0m,
            120m,
            "cash",
            "completed",
            DateTime.UtcNow,
            [],
            []);
        var dispatched = new List<IRequest<SaleReadModel>>();
        sender.Setup(service => service.Send(
                It.IsAny<IRequest<SaleReadModel>>(),
                It.IsAny<CancellationToken>()))
            .Returns<IRequest<SaleReadModel>, CancellationToken>((request, _) =>
            {
                dispatched.Add(request);
                return Task.FromResult(request is VoidSaleCommand ? sale with { Status = "voided" } : sale);
            });
        var controller = CreateController(
            new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "5")],
                "test")),
            sender.Object,
            outletAccess.Object);

        var result = await controller.Void(
            77,
            new VoidSaleDto { Reason = "customer request" },
            CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Collection(
            dispatched,
            request => Assert.IsType<GetSaleByIdQuery>(request),
            request =>
            {
                var command = Assert.IsType<VoidSaleCommand>(request);
                Assert.Equal(77, command.SaleId);
                Assert.Equal("customer request", command.Reason);
                Assert.Equal(5, command.ActorUserId);
            });
        outletAccess.Verify(
            service => service.ResolveAndAuthorizeLocationAsync(5001, "outlet"),
            Times.Once);
    }

    [Fact]
    public async Task Void_DoesNotDispatchCommandWhenSaleOutletIsDenied()
    {
        var outletAccess = new Mock<IUserOutletAccessService>();
        outletAccess.Setup(service => service.ResolveAndAuthorizeLocationAsync(5001, "outlet"))
            .ThrowsAsync(new UnauthorizedAccessException("Outlet denied"));
        var sender = new Mock<ISender>();
        var sale = new SaleReadModel(
            77,
            "S-77",
            5001,
            "Outlet",
            "Address",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            5,
            "Cashier",
            DateTime.UtcNow,
            120m,
            0m,
            0m,
            120m,
            "cash",
            "completed",
            DateTime.UtcNow,
            [],
            []);
        sender.Setup(service => service.Send(
                It.IsAny<IRequest<SaleReadModel>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(sale);
        var controller = CreateController(
            new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "5")],
                "test")),
            sender.Object,
            outletAccess.Object);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => controller.Void(77, new VoidSaleDto { Reason = "request" }, CancellationToken.None));

        sender.Verify(
            service => service.Send(
                It.IsAny<IRequest<SaleReadModel>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        sender.Verify(
            service => service.Send(
                It.Is<IRequest<SaleReadModel>>(request => request is VoidSaleCommand),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static SalesController CreateController(
        ClaimsPrincipal user,
        ISender? sender = null,
        IUserOutletAccessService? outletAccess = null,
        IFeatureEntitlementService? featureEntitlement = null) =>
        new(
            outletAccess ?? Mock.Of<IUserOutletAccessService>(),
            featureEntitlement ?? Mock.Of<IFeatureEntitlementService>(),
            Mock.Of<ILogger<SalesController>>(),
            sender ?? Mock.Of<ISender>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user }
            }
        };

    private static SaleReadModel CreateSale(string status) => new(
        77,
        "S-77",
        5001,
        "Outlet",
        "Address",
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        5,
        "Cashier",
        DateTime.UtcNow,
        120m,
        0m,
        0m,
        120m,
        "cash",
        status,
        DateTime.UtcNow,
        [],
        []);
}
