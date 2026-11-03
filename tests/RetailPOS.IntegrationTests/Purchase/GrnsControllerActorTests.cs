using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using Moq;
using RetailPOS.API.Controllers;
using RetailPOS.API.DTOs.Grn;
using RetailPOS.Application.Grns.Commands;
using RetailPOS.Application.Grns.ReadModels;

namespace RetailPOS.Tests.Purchase;

public class GrnsControllerActorTests
{
    [Fact]
    public async Task Complete_PassesAuthenticatedActor_AndRejectsMissingIdentity()
    {
        var sender = new Mock<ISender>();
        sender
            .Setup(x => x.Send(It.IsAny<CompleteGrnCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GrnReadModel(
                400,
                300,
                20,
                "Supplier",
                10,
                "Warehouse",
                DateTime.UtcNow,
                "completed",
                null,
                null,
                null,
                77,
                null,
                DateTime.UtcNow,
                []));
        var controller = new GrnsController(sender.Object);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        var unauthorized = await controller.Complete(400, CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(unauthorized.Result);
        sender.Verify(
            x => x.Send(It.IsAny<CompleteGrnCommand>(), It.IsAny<CancellationToken>()),
            Times.Never);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, "77")],
                    authenticationType: "test"))
            }
        };
        var completed = await controller.Complete(400, CancellationToken.None);

        Assert.IsType<OkObjectResult>(completed.Result);
        sender.Verify(
            x => x.Send(
                It.Is<CompleteGrnCommand>(command => command.GrnId == 400 && command.ActorUserId == 77),
                It.IsAny<CancellationToken>()),
            Times.Once);
        sender.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Create_PassesAuthenticatedActorAndReturnsCreatedResource()
    {
        var sender = new Mock<ISender>();
        sender
            .Setup(x => x.Send(It.IsAny<CreateGrnCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GrnReadModel(
                400,
                300,
                20,
                "Supplier",
                10,
                "Warehouse",
                DateTime.UtcNow,
                "partial",
                null,
                null,
                null,
                77,
                null,
                DateTime.UtcNow,
                []));
        var controller = new GrnsController(sender.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, "77")],
                        authenticationType: "test"))
                }
            }
        };

        var result = await controller.Create(
            new CreateGrnDto
            {
                PoId = 300,
                Items =
                [
                    new CreateGrnItemDto
                    {
                        PoItemId = 301,
                        ReceivedQty = 2,
                        UnitCost = 8m,
                        Notes = "Two cartons"
                    }
                ]
            },
            CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(nameof(GrnsController.GetById), created.ActionName);
        Assert.Equal(400L, (long)created.RouteValues!["id"]!);
        sender.Verify(
            x => x.Send(
                It.Is<CreateGrnCommand>(command =>
                    command.PurchaseOrderId == 300 &&
                    command.ActorUserId == 77 &&
                    command.Items != null &&
                    command.Items.Count == 1 &&
                    command.Items[0].PurchaseOrderItemId == 301 &&
                    command.Items[0].ReceivedQuantity == 2),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
