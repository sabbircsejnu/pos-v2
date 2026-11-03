using Moq;
using RetailPOS.Application.PurchaseOrders.Abstractions;
using RetailPOS.Application.PurchaseOrders.Queries;
using RetailPOS.Application.PurchaseOrders.ReadModels;

namespace RetailPOS.UnitTests.PurchaseOrders;

public sealed class GetPurchaseOrderByIdQueryHandlerTests
{
    [Fact]
    public async Task Handle_UsesAuthoritativeBusinessScopeForRead()
    {
        var store = new Mock<IPurchaseOrderReadStore>();
        var tenantScope = new Mock<IPurchaseOrderTenantScope>();
        var expected = CreateReadModel();
        using var cancellation = new CancellationTokenSource();
        tenantScope.Setup(scope => scope.GetBusinessIdAsync(cancellation.Token))
            .ReturnsAsync(27);
        store.Setup(port => port.FindByIdAsync(410, 27, cancellation.Token))
            .ReturnsAsync(expected);
        var handler = new GetPurchaseOrderByIdQueryHandler(store.Object, tenantScope.Object);

        var result = await handler.Handle(new GetPurchaseOrderByIdQuery(410), cancellation.Token);

        Assert.Same(expected, result);
        tenantScope.Verify(scope => scope.GetBusinessIdAsync(cancellation.Token), Times.Once);
        store.Verify(port => port.FindByIdAsync(410, 27, cancellation.Token), Times.Once);
    }

    [Fact]
    public async Task Handle_PreservesSuperAdminUnrestrictedScope()
    {
        var store = new Mock<IPurchaseOrderReadStore>();
        var tenantScope = new Mock<IPurchaseOrderTenantScope>();
        var expected = CreateReadModel();
        tenantScope.Setup(scope => scope.GetBusinessIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((long?)null);
        store.Setup(port => port.FindByIdAsync(410, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);
        var handler = new GetPurchaseOrderByIdQueryHandler(store.Object, tenantScope.Object);

        var result = await handler.Handle(new GetPurchaseOrderByIdQuery(410), CancellationToken.None);

        Assert.Same(expected, result);
        store.Verify(port => port.FindByIdAsync(410, null, CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task Handle_UsesExistingNotFoundMessageForMissingOrOutOfScopeOrder()
    {
        var store = new Mock<IPurchaseOrderReadStore>();
        var tenantScope = new Mock<IPurchaseOrderTenantScope>();
        tenantScope.Setup(scope => scope.GetBusinessIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(27);
        store.Setup(port => port.FindByIdAsync(410, 27, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PurchaseOrderReadModel?)null);
        var handler = new GetPurchaseOrderByIdQueryHandler(store.Object, tenantScope.Object);

        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            handler.Handle(new GetPurchaseOrderByIdQuery(410), CancellationToken.None));

        Assert.Equal("Purchase order with ID 410 not found", exception.Message);
    }

    private static PurchaseOrderReadModel CreateReadModel() =>
        new(
            410,
            "PO-20260613-0410",
            12,
            "Supplier",
            18,
            "Warehouse",
            new DateTime(2026, 6, 13),
            null,
            75.44m,
            "draft",
            null,
            null,
            9,
            "Buyer",
            new DateTime(2026, 6, 13),
            new DateTime(2026, 6, 13),
            null,
            []);
}
