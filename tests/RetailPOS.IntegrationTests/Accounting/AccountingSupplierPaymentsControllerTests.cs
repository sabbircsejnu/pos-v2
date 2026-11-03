using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using Moq;
using RetailPOS.API.Controllers;
using RetailPOS.API.DTOs.SupplierPayment;
using RetailPOS.API.Models;
using RetailPOS.Application.SupplierPayments.Commands;
using RetailPOS.Application.SupplierPayments.ReadModels;

namespace RetailPOS.Tests.Accounting;

public sealed class AccountingSupplierPaymentsControllerTests
{
    [Fact]
    public async Task CreatePayment_RequiresActorAndDispatchesApplicationCommand()
    {
        var sender = new Mock<ISender>();
        sender.Setup(value => value.Send(
                It.IsAny<CreateSupplierPaymentCommand>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SupplierPaymentReadModel(
                500, 10, "Supplier", 7, new DateTime(2026, 3, 15), null,
                "cash", 401, 50m, 0m, 0m, "draft", "PAY-1", null, null));
        var controller = new AccountingSupplierPaymentsController(sender.Object);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        var unauthorized = await controller.CreatePayment(new CreateSupplierPaymentDto(), CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(unauthorized.Result);
        sender.Verify(value => value.Send(
            It.IsAny<CreateSupplierPaymentCommand>(),
            It.IsAny<CancellationToken>()), Times.Never);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, "101")],
                    authenticationType: "test"))
            }
        };
        var result = await controller.CreatePayment(
            new CreateSupplierPaymentDto
            {
                SupplierId = 10,
                OutletId = 7,
                PaymentDate = new DateTime(2026, 3, 15),
                PaymentMethod = "cash",
                PaymentAccountId = 401,
                Amount = 50m,
                ReferenceNumber = "PAY-1",
                Allocations = [new SupplierPaymentAllocationLineDto { BillId = 200, Amount = 10m }]
            },
            CancellationToken.None);

        var created = Assert.IsType<CreatedResult>(result.Result);
        Assert.Equal(string.Empty, created.Location);
        sender.Verify(value => value.Send(
            It.Is<CreateSupplierPaymentCommand>(command =>
                command.SupplierId == 10 &&
                command.ActorUserId == 101 &&
                command.OutletId == 7 &&
                command.Allocations!.Count == 1 &&
                command.Allocations[0].BillId == 200),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SubmitPayment_RequiresActorAndDispatchesApplicationCommand()
    {
        var sender = new Mock<ISender>();
        sender.Setup(value => value.Send(
                It.IsAny<SubmitSupplierPaymentCommand>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SupplierPaymentReadModel(
                500, 10, "Supplier", 7, new DateTime(2026, 3, 15), null,
                "cash", 401, 50m, 0m, 0m, "submitted", "PAY-1", null, null));
        var controller = new AccountingSupplierPaymentsController(sender.Object);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        var unauthorized = await controller.SubmitPayment(500, CancellationToken.None);
        Assert.IsType<UnauthorizedObjectResult>(unauthorized.Result);
        sender.Verify(value => value.Send(
            It.IsAny<SubmitSupplierPaymentCommand>(),
            It.IsAny<CancellationToken>()), Times.Never);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, "101")],
                    authenticationType: "test"))
            }
        };
        using var cancellation = new CancellationTokenSource();
        var result = await controller.SubmitPayment(500, cancellation.Token);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ApiResponse<SupplierPaymentDto>>(ok.Value);
        Assert.True(response.Success);
        Assert.Equal("Supplier payment submitted successfully", response.Message);
        Assert.Equal("submitted", response.Data?.Status);
        sender.Verify(value => value.Send(
            It.Is<SubmitSupplierPaymentCommand>(command =>
                command.PaymentId == 500 && command.ActorUserId == 101),
            cancellation.Token), Times.Once);
    }

    [Fact]
    public async Task ApprovePayment_PreservesActorAndSuccessEnvelope()
    {
        var sender = new Mock<ISender>();
        sender.Setup(value => value.Send(
                It.IsAny<ApproveSupplierPaymentCommand>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SupplierPaymentReadModel(
                500, 10, "Supplier", 7, new DateTime(2026, 3, 15), null,
                "cash", 401, 50m, 50m, 0m, "posted", "PAY-1", null, 700));
        var controller = new AccountingSupplierPaymentsController(sender.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, "101")],
                        authenticationType: "test"))
                }
            }
        };

        using var cancellation = new CancellationTokenSource();
        var result = await controller.ApprovePayment(500, cancellation.Token);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ApiResponse<SupplierPaymentDto>>(ok.Value);
        Assert.True(response.Success);
        Assert.Equal("Supplier payment approved successfully", response.Message);
        Assert.Equal(700, response.Data?.JournalEntryId);
        sender.Verify(value => value.Send(
            It.Is<ApproveSupplierPaymentCommand>(command =>
                command.PaymentId == 500 && command.ActorUserId == 101),
            cancellation.Token), Times.Once);
    }

    [Fact]
    public async Task ReversePayment_RequiresActorAndPreservesSuccessEnvelope()
    {
        var sender = new Mock<ISender>();
        sender.Setup(value => value.Send(
                It.IsAny<ReverseSupplierPaymentCommand>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SupplierPaymentReadModel(
                500, 10, "Supplier", 7, new DateTime(2026, 3, 15), new DateTime(2026, 3, 15),
                "cash", 401, 50m, 0m, 0m, "reversed", "PAY-1", null, 700));
        var controller = new AccountingSupplierPaymentsController(sender.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        var request = new ReverseSupplierPaymentDto
        {
            PostingDate = new DateTime(2026, 4, 15),
            Reason = " correction "
        };

        var unauthorized = await controller.ReversePayment(500, request, CancellationToken.None);
        Assert.IsType<UnauthorizedObjectResult>(unauthorized.Result);
        sender.Verify(value => value.Send(
            It.IsAny<ReverseSupplierPaymentCommand>(),
            It.IsAny<CancellationToken>()), Times.Never);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, "101")],
                    authenticationType: "test"))
            }
        };
        using var cancellation = new CancellationTokenSource();
        var result = await controller.ReversePayment(500, request, cancellation.Token);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ApiResponse<SupplierPaymentDto>>(ok.Value);
        Assert.True(response.Success);
        Assert.Equal("Supplier payment reversed successfully", response.Message);
        Assert.Equal("reversed", response.Data?.Status);
        sender.Verify(value => value.Send(
            It.Is<ReverseSupplierPaymentCommand>(command =>
                command.PaymentId == 500
                && command.ActorUserId == 101
                && command.PostingDate == request.PostingDate
                && command.Reason == request.Reason),
            cancellation.Token), Times.Once);
    }

    [Fact]
    public async Task CreateAdvance_RequiresActorAndDispatchesApplicationCommand()
    {
        var sender = new Mock<ISender>();
        sender.Setup(value => value.Send(
                It.IsAny<CreateSupplierAdvanceCommand>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SupplierAdvanceReadModel(
                800, 10, "Supplier", 7, new DateTime(2026, 3, 15),
                "cash", 401, 50m, 50m, 0m, "draft", "ADV-1", null, null));
        var controller = new AccountingSupplierPaymentsController(sender.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        var request = new CreateSupplierAdvanceDto
        {
            SupplierId = 10,
            OutletId = 7,
            PaymentDate = new DateTime(2026, 3, 15),
            PaymentMethod = "cash",
            PaymentAccountId = 401,
            Amount = 50m,
            ReferenceNumber = "ADV-1"
        };

        var unauthorized = await controller.CreateAdvance(request, CancellationToken.None);
        Assert.IsType<UnauthorizedObjectResult>(unauthorized.Result);
        sender.Verify(value => value.Send(
            It.IsAny<CreateSupplierAdvanceCommand>(),
            It.IsAny<CancellationToken>()), Times.Never);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, "101")],
                    authenticationType: "test"))
            }
        };
        using var cancellation = new CancellationTokenSource();
        var result = await controller.CreateAdvance(request, cancellation.Token);

        var created = Assert.IsType<CreatedResult>(result.Result);
        Assert.Equal(string.Empty, created.Location);
        var response = Assert.IsType<ApiResponse<SupplierAdvanceDto>>(created.Value);
        Assert.True(response.Success);
        Assert.Equal("Supplier advance created successfully", response.Message);
        Assert.Equal(800, response.Data?.Id);
        sender.Verify(value => value.Send(
            It.Is<CreateSupplierAdvanceCommand>(command =>
                command.SupplierId == 10 &&
                command.OutletId == 7 &&
                command.ActorUserId == 101 &&
                command.ReferenceNumber == "ADV-1"),
            cancellation.Token), Times.Once);
    }

    [Fact]
    public async Task SubmitAdvance_RequiresActorAndPreservesSuccessEnvelope()
    {
        var sender = new Mock<ISender>();
        sender.Setup(value => value.Send(
                It.IsAny<SubmitSupplierAdvanceCommand>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SupplierAdvanceReadModel(
                800, 10, "Supplier", 7, new DateTime(2026, 3, 15),
                "cash", 401, 50m, 50m, 0m, "submitted", "ADV-1", null, null));
        var controller = new AccountingSupplierPaymentsController(sender.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        var unauthorized = await controller.SubmitAdvance(800, CancellationToken.None);
        Assert.IsType<UnauthorizedObjectResult>(unauthorized.Result);
        sender.Verify(value => value.Send(
            It.IsAny<SubmitSupplierAdvanceCommand>(),
            It.IsAny<CancellationToken>()), Times.Never);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, "101")],
                    authenticationType: "test"))
            }
        };
        using var cancellation = new CancellationTokenSource();
        var result = await controller.SubmitAdvance(800, cancellation.Token);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ApiResponse<SupplierAdvanceDto>>(ok.Value);
        Assert.True(response.Success);
        Assert.Equal("Supplier advance submitted successfully", response.Message);
        Assert.Equal("submitted", response.Data?.Status);
        sender.Verify(value => value.Send(
            It.Is<SubmitSupplierAdvanceCommand>(command =>
                command.AdvanceId == 800 && command.ActorUserId == 101),
            cancellation.Token), Times.Once);
    }

    [Fact]
    public async Task ApproveAdvance_RequiresActorAndPreservesSuccessEnvelope()
    {
        var sender = new Mock<ISender>();
        sender.Setup(value => value.Send(
                It.IsAny<ApproveSupplierAdvanceCommand>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SupplierAdvanceReadModel(
                800, 10, "Supplier", 7, new DateTime(2026, 3, 15),
                "cash", 401, 50m, 50m, 0m, "posted", "ADV-1", null, 900));
        var controller = new AccountingSupplierPaymentsController(sender.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        var unauthorized = await controller.ApproveAdvance(800, CancellationToken.None);
        Assert.IsType<UnauthorizedObjectResult>(unauthorized.Result);
        sender.Verify(value => value.Send(
            It.IsAny<ApproveSupplierAdvanceCommand>(),
            It.IsAny<CancellationToken>()), Times.Never);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, "101")],
                    authenticationType: "test"))
            }
        };
        using var cancellation = new CancellationTokenSource();
        var result = await controller.ApproveAdvance(800, cancellation.Token);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ApiResponse<SupplierAdvanceDto>>(ok.Value);
        Assert.True(response.Success);
        Assert.Equal("Supplier advance approved successfully", response.Message);
        Assert.Equal(900, response.Data?.JournalEntryId);
        sender.Verify(value => value.Send(
            It.Is<ApproveSupplierAdvanceCommand>(command =>
                command.AdvanceId == 800 && command.ActorUserId == 101),
            cancellation.Token), Times.Once);
    }

    [Fact]
    public async Task ApplyAdvance_RequiresActorAndPreservesSuccessEnvelope()
    {
        var sender = new Mock<ISender>();
        sender.Setup(value => value.Send(
                It.IsAny<ApplySupplierAdvanceCommand>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var controller = new AccountingSupplierPaymentsController(sender.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        var request = new ApplySupplierAdvanceDto
        {
            SupplierPayableDocumentId = 200,
            Amount = 30m,
            EffectiveDate = new DateTime(2026, 3, 15),
            ReferenceNumber = "UTIL-1",
            Notes = "Apply"
        };

        var unauthorized = await controller.ApplyAdvance(800, request, CancellationToken.None);
        Assert.IsType<UnauthorizedObjectResult>(unauthorized.Result);
        sender.Verify(value => value.Send(
            It.IsAny<ApplySupplierAdvanceCommand>(),
            It.IsAny<CancellationToken>()), Times.Never);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, "101")],
                    authenticationType: "test"))
            }
        };
        using var cancellation = new CancellationTokenSource();
        var result = await controller.ApplyAdvance(800, request, cancellation.Token);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ApiResponse<object>>(ok.Value);
        Assert.True(response.Success);
        Assert.Equal("Supplier advance applied successfully", response.Message);
        sender.Verify(value => value.Send(
            It.Is<ApplySupplierAdvanceCommand>(command =>
                command.AdvanceId == 800
                && command.SupplierPayableDocumentId == 200
                && command.Amount == 30m
                && command.EffectiveDate == request.EffectiveDate
                && command.ReferenceNumber == request.ReferenceNumber
                && command.Notes == request.Notes
                && command.ActorUserId == 101),
            cancellation.Token), Times.Once);
    }

    [Fact]
    public async Task ReverseAdvanceUtilization_RequiresActorAndPreservesSuccessEnvelope()
    {
        var sender = new Mock<ISender>();
        sender.Setup(value => value.Send(
                It.IsAny<ReverseSupplierAdvanceUtilizationCommand>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var controller = new AccountingSupplierPaymentsController(sender.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        var request = new ReverseSupplierAdvanceUtilizationDto
        {
            PostingDate = new DateTime(2026, 3, 20),
            Reason = "corrected supplier credit"
        };

        var unauthorized = await controller.ReverseAdvanceUtilization(800, request, CancellationToken.None);
        Assert.IsType<UnauthorizedObjectResult>(unauthorized.Result);
        sender.Verify(value => value.Send(
            It.IsAny<ReverseSupplierAdvanceUtilizationCommand>(),
            It.IsAny<CancellationToken>()), Times.Never);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, "101")],
                    authenticationType: "test"))
            }
        };
        using var cancellation = new CancellationTokenSource();
        var result = await controller.ReverseAdvanceUtilization(800, request, cancellation.Token);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ApiResponse<object>>(ok.Value);
        Assert.True(response.Success);
        Assert.Equal("Supplier advance utilization reversed successfully", response.Message);
        sender.Verify(value => value.Send(
            It.Is<ReverseSupplierAdvanceUtilizationCommand>(command =>
                command.UtilizationId == 800
                && command.PostingDate == request.PostingDate
                && command.Reason == request.Reason
                && command.ActorUserId == 101),
            cancellation.Token), Times.Once);
    }
}
