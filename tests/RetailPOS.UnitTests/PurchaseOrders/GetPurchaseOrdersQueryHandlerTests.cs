using Moq;
using RetailPOS.Application.PurchaseOrders.Abstractions;
using RetailPOS.Application.PurchaseOrders.Queries;
using RetailPOS.Application.PurchaseOrders.ReadModels;

namespace RetailPOS.UnitTests.PurchaseOrders;

public sealed class GetPurchaseOrdersQueryHandlerTests
{
    [Fact]
    public async Task Handle_ForwardsAllFiltersAndAuthoritativeBusinessScope()
    {
        var store = new Mock<IPurchaseOrderReadStore>();
        var tenantScope = new Mock<IPurchaseOrderTenantScope>();
        IReadOnlyList<PurchaseOrderReadModel> expected = [];
        using var cancellation = new CancellationTokenSource();
        tenantScope.Setup(scope => scope.GetBusinessIdAsync(cancellation.Token))
            .ReturnsAsync(27);
        store.Setup(port => port.FindAllAsync("draft", 12, "warehouse", 18, 27, cancellation.Token))
            .ReturnsAsync(expected);
        var handler = new GetPurchaseOrdersQueryHandler(store.Object, tenantScope.Object);

        var result = await handler.Handle(
            new GetPurchaseOrdersQuery("draft", 12, 18),
            cancellation.Token);

        Assert.Same(expected, result);
        store.Verify(port => port.FindAllAsync("draft", 12, "warehouse", 18, 27, cancellation.Token), Times.Once);
    }

    [Fact]
    public async Task Handle_PreservesUnrestrictedSuperAdminScope()
    {
        var store = new Mock<IPurchaseOrderReadStore>();
        var tenantScope = new Mock<IPurchaseOrderTenantScope>();
        tenantScope.Setup(scope => scope.GetBusinessIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((long?)null);
        store.Setup(port => port.FindAllAsync(null, null, null, null, null, CancellationToken.None))
            .ReturnsAsync(Array.Empty<PurchaseOrderReadModel>());
        var handler = new GetPurchaseOrdersQueryHandler(store.Object, tenantScope.Object);

        var result = await handler.Handle(
            new GetPurchaseOrdersQuery(null, null, null),
            CancellationToken.None);

        Assert.Empty(result);
        store.Verify(
            port => port.FindAllAsync(null, null, null, null, null, CancellationToken.None),
            Times.Once);
    }
}
