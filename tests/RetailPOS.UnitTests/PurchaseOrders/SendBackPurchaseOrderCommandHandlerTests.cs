using Moq;
using RetailPOS.Application.PurchaseOrders.Abstractions;
using RetailPOS.Application.PurchaseOrders.Commands;
using RetailPOS.Application.PurchaseOrders.ReadModels;
using RetailPOS.Core.Entities;

namespace RetailPOS.UnitTests.PurchaseOrders;

public sealed class SendBackPurchaseOrderCommandHandlerTests
{
    [Fact]
    public async Task Handle_SendsPendingOrderBackWithReasonWithinTenantScope()
    {
        var updateStore = new Mock<IPurchaseOrderUpdateStore>();
        var readStore = new Mock<IPurchaseOrderReadStore>();
        var tenantScope = new Mock<IPurchaseOrderTenantScope>();
        var existing = new PurchaseOrder { Id = 42, Status = "PENDING" };
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
            "sent_back",
            "Correct quantities",
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
                    order.Status == "sent_back" &&
                    order.RejectionReason == "Correct quantities"),
                cancellation.Token))
            .Returns(Task.CompletedTask);
        readStore.Setup(store => store.FindByIdAsync(42, 27, cancellation.Token))
            .ReturnsAsync(expected);
        var handler = new SendBackPurchaseOrderCommandHandler(
            updateStore.Object,
            readStore.Object,
            tenantScope.Object);

        var result = await handler.Handle(
            new SendBackPurchaseOrderCommand(42, "Correct quantities"),
            cancellation.Token);

        Assert.Same(expected, result);
        updateStore.Verify(store => store.SaveUpdateAsync(
            existing,
            cancellation.Token), Times.Once);
    }

    [Fact]
    public async Task Handle_RejectsNonPendingOrderWithoutPersistence()
    {
        var updateStore = new Mock<IPurchaseOrderUpdateStore>();
        var readStore = new Mock<IPurchaseOrderReadStore>();
        var tenantScope = new Mock<IPurchaseOrderTenantScope>();
        using var cancellation = new CancellationTokenSource();
        tenantScope.Setup(scope => scope.GetBusinessIdAsync(cancellation.Token))
            .ReturnsAsync(27);
        updateStore.Setup(store => store.FindForUpdateAsync(42, 27, cancellation.Token))
            .ReturnsAsync(new PurchaseOrder { Id = 42, Status = "draft" });
        var handler = new SendBackPurchaseOrderCommandHandler(
            updateStore.Object,
            readStore.Object,
            tenantScope.Object);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(
                new SendBackPurchaseOrderCommand(42, "Not pending"),
                cancellation.Token));

        Assert.Equal(
            "Can only send back pending purchase orders. Current status: draft",
            exception.Message);
        updateStore.Verify(store => store.SaveUpdateAsync(
            It.IsAny<PurchaseOrder>(),
            It.IsAny<CancellationToken>()), Times.Never);
        readStore.Verify(store => store.FindByIdAsync(
            It.IsAny<long>(),
            It.IsAny<long?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }
}
