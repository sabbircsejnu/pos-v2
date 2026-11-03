using Moq;
using RetailPOS.Application.PurchaseOrders.Abstractions;
using RetailPOS.Application.PurchaseOrders.Commands;
using RetailPOS.Application.PurchaseOrders.ReadModels;
using RetailPOS.Core.Entities;

namespace RetailPOS.UnitTests.PurchaseOrders;

public sealed class ApprovePurchaseOrderCommandHandlerTests
{
    [Fact]
    public async Task Handle_ApprovesPendingOrderWithinBusinessScope()
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
            "approved",
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
        updateStore.Setup(store => store.SaveStatusUpdateAsync(
                existing,
                "approved",
                cancellation.Token))
            .Returns(Task.CompletedTask);
        readStore.Setup(store => store.FindByIdAsync(42, 27, cancellation.Token))
            .ReturnsAsync(expected);
        var handler = new ApprovePurchaseOrderCommandHandler(
            updateStore.Object,
            readStore.Object,
            tenantScope.Object);

        var result = await handler.Handle(
            new ApprovePurchaseOrderCommand(42),
            cancellation.Token);

        Assert.Same(expected, result);
        updateStore.Verify(store => store.SaveStatusUpdateAsync(
            existing,
            "approved",
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
        var handler = new ApprovePurchaseOrderCommandHandler(
            updateStore.Object,
            readStore.Object,
            tenantScope.Object);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new ApprovePurchaseOrderCommand(42), cancellation.Token));

        Assert.Equal(
            "Can only approve pending purchase orders. Current status: draft",
            exception.Message);
        updateStore.Verify(store => store.SaveStatusUpdateAsync(
            It.IsAny<PurchaseOrder>(),
            It.IsAny<string>(),
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
        var handler = new ApprovePurchaseOrderCommandHandler(
            updateStore.Object,
            readStore.Object,
            tenantScope.Object);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            handler.Handle(new ApprovePurchaseOrderCommand(42), cancellation.Token));

        updateStore.Verify(store => store.SaveStatusUpdateAsync(
            It.IsAny<PurchaseOrder>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
        readStore.Verify(store => store.FindByIdAsync(
            It.IsAny<long>(),
            It.IsAny<long?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }
}
