using Moq;
using RetailPOS.Application.PurchaseOrders.Abstractions;
using RetailPOS.Application.PurchaseOrders.Commands;
using RetailPOS.Application.PurchaseOrders.ReadModels;
using RetailPOS.Core.Entities;

namespace RetailPOS.UnitTests.PurchaseOrders;

public sealed class SubmitPurchaseOrderForApprovalCommandHandlerTests
{
    [Fact]
    public async Task Handle_SubmitsSentBackOrderClearsReasonAndUsesBusinessScope()
    {
        var updateStore = new Mock<IPurchaseOrderUpdateStore>();
        var readStore = new Mock<IPurchaseOrderReadStore>();
        var tenantScope = new Mock<IPurchaseOrderTenantScope>();
        var existing = new PurchaseOrder
        {
            Id = 42,
            Status = "sent_back",
            RejectionReason = "Correct the quantity"
        };
        var expected = new PurchaseOrderReadModel(
            42,
            "PO-20260612-0042",
            1001,
            "Acme Supplier",
            1101,
            "Central Warehouse",
            DateTime.UtcNow,
            null,
            40m,
            "pending",
            null,
            null,
            321,
            "Test User",
            DateTime.UtcNow,
            DateTime.UtcNow,
            null,
            []);
        using var cancellation = new CancellationTokenSource();
        tenantScope.Setup(scope => scope.GetBusinessIdAsync(cancellation.Token))
            .ReturnsAsync(27);
        updateStore.Setup(store => store.FindForUpdateAsync(42, 27, cancellation.Token))
            .ReturnsAsync(existing);
        updateStore.Setup(store => store.SaveUpdateAsync(
                It.Is<PurchaseOrder>(order =>
                    order.Status == "pending" &&
                    order.RejectionReason == null),
                cancellation.Token))
            .Returns(Task.CompletedTask);
        readStore.Setup(store => store.FindByIdAsync(42, 27, cancellation.Token))
            .ReturnsAsync(expected);
        var handler = new SubmitPurchaseOrderForApprovalCommandHandler(
            updateStore.Object,
            readStore.Object,
            tenantScope.Object);

        var result = await handler.Handle(
            new SubmitPurchaseOrderForApprovalCommand(42),
            cancellation.Token);

        Assert.Same(expected, result);
        updateStore.Verify(store => store.SaveUpdateAsync(
            It.IsAny<PurchaseOrder>(),
            cancellation.Token), Times.Once);
    }
}
