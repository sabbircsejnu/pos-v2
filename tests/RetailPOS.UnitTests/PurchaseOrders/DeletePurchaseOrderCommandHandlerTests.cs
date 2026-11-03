using Moq;
using RetailPOS.Application.PurchaseOrders.Abstractions;
using RetailPOS.Application.PurchaseOrders.Commands;

namespace RetailPOS.UnitTests.PurchaseOrders;

public sealed class DeletePurchaseOrderCommandHandlerTests
{
    [Fact]
    public async Task Handle_DeletesEligibleOrderForCurrentBusiness()
    {
        var deletionStore = new Mock<IPurchaseOrderDeletionStore>();
        var tenantScope = new Mock<IPurchaseOrderTenantScope>();
        using var cancellation = new CancellationTokenSource();
        tenantScope.Setup(scope => scope.GetBusinessIdAsync(cancellation.Token))
            .ReturnsAsync(27);
        deletionStore.Setup(store => store.CanDeleteAsync(42, 27, cancellation.Token))
            .ReturnsAsync(true);
        deletionStore.Setup(store => store.DeleteAsync(42, 27, cancellation.Token))
            .ReturnsAsync(true);
        var handler = new DeletePurchaseOrderCommandHandler(
            deletionStore.Object,
            tenantScope.Object);

        var result = await handler.Handle(
            new DeletePurchaseOrderCommand(42),
            cancellation.Token);

        Assert.True(result);
        deletionStore.Verify(store => store.DeleteAsync(42, 27, cancellation.Token), Times.Once);
    }

    [Fact]
    public async Task Handle_RejectsOrderWhenStatusOrGrnPreventsDeletion()
    {
        var deletionStore = new Mock<IPurchaseOrderDeletionStore>();
        var tenantScope = new Mock<IPurchaseOrderTenantScope>();
        using var cancellation = new CancellationTokenSource();
        tenantScope.Setup(scope => scope.GetBusinessIdAsync(cancellation.Token))
            .ReturnsAsync(27);
        deletionStore.Setup(store => store.CanDeleteAsync(42, 27, cancellation.Token))
            .ReturnsAsync(false);
        var handler = new DeletePurchaseOrderCommandHandler(
            deletionStore.Object,
            tenantScope.Object);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new DeletePurchaseOrderCommand(42), cancellation.Token));

        Assert.Equal(
            "Cannot delete purchase order: either it's not in draft/pending status or has associated GRNs",
            exception.Message);
        deletionStore.Verify(store => store.DeleteAsync(
            It.IsAny<long>(),
            It.IsAny<long?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }
}
