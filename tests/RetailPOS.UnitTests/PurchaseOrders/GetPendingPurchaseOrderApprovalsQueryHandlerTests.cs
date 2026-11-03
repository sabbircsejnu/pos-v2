using Moq;
using RetailPOS.Application.PurchaseOrders.Abstractions;
using RetailPOS.Application.PurchaseOrders.Queries;
using RetailPOS.Application.PurchaseOrders.ReadModels;

namespace RetailPOS.UnitTests.PurchaseOrders;

public sealed class GetPendingPurchaseOrderApprovalsQueryHandlerTests
{
    [Fact]
    public async Task Handle_ForwardsAuthoritativeBusinessScopeAndCancellation()
    {
        var store = new Mock<IPurchaseOrderReadStore>();
        var tenantScope = new Mock<IPurchaseOrderTenantScope>();
        IReadOnlyList<PurchaseOrderReadModel> expected = [];
        using var cancellation = new CancellationTokenSource();
        tenantScope.Setup(scope => scope.GetBusinessIdAsync(cancellation.Token))
            .ReturnsAsync(27);
        store.Setup(port => port.FindPendingApprovalsAsync(27, cancellation.Token))
            .ReturnsAsync(expected);
        var handler = new GetPendingPurchaseOrderApprovalsQueryHandler(
            store.Object,
            tenantScope.Object);

        var result = await handler.Handle(
            new GetPendingPurchaseOrderApprovalsQuery(),
            cancellation.Token);

        Assert.Same(expected, result);
        store.Verify(
            port => port.FindPendingApprovalsAsync(27, cancellation.Token),
            Times.Once);
    }
}
