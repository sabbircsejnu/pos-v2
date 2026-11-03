using Moq;
using RetailPOS.Application.PurchaseOrders.Abstractions;
using RetailPOS.Application.PurchaseOrders.Commands;
using RetailPOS.Application.PurchaseOrders.ReadModels;
using RetailPOS.Core.Entities;

namespace RetailPOS.UnitTests.PurchaseOrders;

public sealed class RejectPurchaseOrderCommandHandlerTests
{
    [Fact]
    public async Task Handle_RejectsPendingOrderAndPreservesReasonAndTenantScope()
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
            "rejected",
            "Price is above limit",
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
                    order.Status == "rejected" &&
                    order.RejectionReason == "Price is above limit"),
                cancellation.Token))
            .Returns(Task.CompletedTask);
        readStore.Setup(store => store.FindByIdAsync(42, 27, cancellation.Token))
            .ReturnsAsync(expected);
        var handler = new RejectPurchaseOrderCommandHandler(
            updateStore.Object,
            readStore.Object,
            tenantScope.Object);

        var result = await handler.Handle(
            new RejectPurchaseOrderCommand(42, "Price is above limit"),
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
        var handler = new RejectPurchaseOrderCommandHandler(
            updateStore.Object,
            readStore.Object,
            tenantScope.Object);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(
                new RejectPurchaseOrderCommand(42, "Not pending"),
                cancellation.Token));

        Assert.Equal(
            "Can only reject pending purchase orders. Current status: draft",
            exception.Message);
        updateStore.Verify(store => store.SaveUpdateAsync(
            It.IsAny<PurchaseOrder>(),
            It.IsAny<CancellationToken>()), Times.Never);
        readStore.Verify(store => store.FindByIdAsync(
            It.IsAny<long>(),
            It.IsAny<long?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ReturnsNotFoundForOrderOutsideTenantScope()
    {
        var updateStore = new Mock<IPurchaseOrderUpdateStore>();
        var readStore = new Mock<IPurchaseOrderReadStore>();
        var tenantScope = new Mock<IPurchaseOrderTenantScope>();
        using var cancellation = new CancellationTokenSource();
        tenantScope.Setup(scope => scope.GetBusinessIdAsync(cancellation.Token))
            .ReturnsAsync(27);
        updateStore.Setup(store => store.FindForUpdateAsync(42, 27, cancellation.Token))
            .ReturnsAsync((PurchaseOrder?)null);
        var handler = new RejectPurchaseOrderCommandHandler(
            updateStore.Object,
            readStore.Object,
            tenantScope.Object);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            handler.Handle(
                new RejectPurchaseOrderCommand(42, "Reason"),
                cancellation.Token));

        updateStore.Verify(store => store.SaveUpdateAsync(
            It.IsAny<PurchaseOrder>(),
            It.IsAny<CancellationToken>()), Times.Never);
        readStore.Verify(store => store.FindByIdAsync(
            It.IsAny<long>(),
            It.IsAny<long?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }
}
