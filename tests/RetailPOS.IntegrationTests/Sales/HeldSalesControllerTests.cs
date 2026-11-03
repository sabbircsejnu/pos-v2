using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System.Security.Claims;
using RetailPOS.API.Controllers;
using RetailPOS.API.DTOs.Sale;
using RetailPOS.API.Models;
using RetailPOS.API.Services;
using RetailPOS.Application.Sales.Commands;
using RetailPOS.Application.Sales.ReadModels;

namespace RetailPOS.Tests.Sales;

public sealed class HeldSalesControllerTests
{
    [Fact]
    public async Task Hold_PreservesFeatureCheckCreatedEnvelopeAndApplicationMapping()
    {
        var featureEntitlement = new Mock<IFeatureEntitlementService>();
        featureEntitlement.Setup(service => service.EnsureFeatureEnabledAsync("sales.hold"))
            .Returns(Task.CompletedTask);
        var sender = new Mock<ISender>();
        var readModel = new HeldSaleReadModel(
            41,
            11,
            "Outlet one",
            7,
            "Cashier seven",
            5,
            "Customer five",
            [new HeldSaleItemReadModel
            {
                VariantId = 9,
                ProductName = "Widget",
                VariantSku = "W-9",
                Quantity = 2,
                UnitPrice = 12m,
                DiscountAmount = 1m,
                AppliedRuleName = "Promo"
            }],
            5m,
            "cash",
            "note",
            DateTime.UtcNow,
            23m);
        IRequest<HeldSaleReadModel>? dispatched = null;
        sender.Setup(service => service.Send(
                It.IsAny<IRequest<HeldSaleReadModel>>(),
                It.IsAny<CancellationToken>()))
            .Callback<IRequest<HeldSaleReadModel>, CancellationToken>((request, _) => dispatched = request)
            .ReturnsAsync(readModel);

        var controller = CreateController(sender.Object, featureEntitlement.Object, userId: 7);
        var request = new HoldSaleDto
        {
            OutletId = 11,
            CashierId = 7,
            CustomerId = 5,
            Items =
            [
                new HeldSaleItemDto
                {
                    VariantId = 9,
                    ProductName = "Widget",
                    VariantSku = "W-9",
                    Quantity = 2,
                    UnitPrice = 12m,
                    DiscountAmount = 1m,
                    AppliedRuleName = "Promo"
                }
            ],
            DiscountPercent = 5m,
            PaymentMethod = "CASH",
            Note = "note"
        };

        var result = await controller.Hold(request, CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(nameof(HeldSalesController.GetById), created.ActionName);
        Assert.Equal(41L, created.RouteValues?["id"]);
        var response = Assert.IsType<ApiResponse<HeldSaleDto>>(created.Value);
        Assert.True(response.Success);
        Assert.Equal("Sale held successfully", response.Message);
        Assert.Equal("Outlet one", response.Data?.OutletName);
        Assert.Equal(23m, response.Data?.Subtotal);
        var command = Assert.IsType<HoldSaleCommand>(dispatched);
        Assert.Equal(11, command.OutletId);
        Assert.Equal(7, command.CashierId);
        Assert.Equal("Promo", Assert.Single(command.Items).AppliedRuleName);
        featureEntitlement.Verify(service => service.EnsureFeatureEnabledAsync("sales.hold"), Times.Once);
    }

    [Fact]
    public async Task Hold_DerivesTheCashierFromThePrincipalAndRejectsASubmittedDifferentCashier()
    {
        var featureEntitlement = new Mock<IFeatureEntitlementService>();
        var sender = new Mock<ISender>();
        IRequest<HeldSaleReadModel>? dispatched = null;
        sender.Setup(service => service.Send(
                It.IsAny<IRequest<HeldSaleReadModel>>(),
                It.IsAny<CancellationToken>()))
            .Callback<IRequest<HeldSaleReadModel>, CancellationToken>((request, _) => dispatched = request)
            .ReturnsAsync(new HeldSaleReadModel(1, 11, "Outlet", 7, "Cashier", null, null, [], 0m, "cash", null, DateTime.UtcNow, 0m));
        var controller = CreateController(sender.Object, featureEntitlement.Object, userId: 7);
        var item = new HeldSaleItemDto { VariantId = 9, ProductName = "Widget", VariantSku = "W-9", Quantity = 1, UnitPrice = 5m };

        var spoofed = await controller.Hold(
            new HoldSaleDto { OutletId = 11, CashierId = 99, Items = [item] },
            CancellationToken.None);
        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(spoofed.Result).StatusCode);
        Assert.Null(dispatched);

        var omitted = await controller.Hold(
            new HoldSaleDto { OutletId = 11, Items = [item] },
            CancellationToken.None);
        Assert.IsType<CreatedAtActionResult>(omitted.Result);
        Assert.Equal(7, Assert.IsType<HoldSaleCommand>(dispatched).CashierId);
    }

    [Fact]
    public async Task GetByCashier_RefusesToListAnotherCashiersHeldSales()
    {
        var sender = new Mock<ISender>();
        var controller = CreateController(sender.Object, Mock.Of<IFeatureEntitlementService>(), userId: 7);

        var result = await controller.GetByCashier(8, CancellationToken.None);

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(result.Result).StatusCode);
        sender.Verify(
            service => service.Send(It.IsAny<IRequest<HeldSaleListReadModel>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static HeldSalesController CreateController(
        ISender sender,
        IFeatureEntitlementService featureEntitlement,
        long userId) => new(sender, featureEntitlement)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, userId.ToString())],
                        "test"))
                }
            }
        };
}
