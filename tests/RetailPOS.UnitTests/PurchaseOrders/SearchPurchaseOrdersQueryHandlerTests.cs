using Moq;
using RetailPOS.Application.PurchaseOrders.Abstractions;
using RetailPOS.Application.PurchaseOrders.Queries;
using RetailPOS.Application.PurchaseOrders.ReadModels;

namespace RetailPOS.UnitTests.PurchaseOrders;

public sealed class SearchPurchaseOrdersQueryHandlerTests
{
    [Fact]
    public async Task Handle_ForwardsSearchFiltersPagingSortAndAuthoritativeBusinessScope()
    {
        var store = new Mock<IPurchaseOrderReadStore>();
        var tenantScope = new Mock<IPurchaseOrderTenantScope>();
        var startDate = new DateTime(2026, 1, 1);
        var endDate = new DateTime(2026, 1, 31);
        var expected = new PurchaseOrderSearchReadModel([], 23, 3, 5);
        using var cancellation = new CancellationTokenSource();
        tenantScope.Setup(scope => scope.GetBusinessIdAsync(cancellation.Token))
            .ReturnsAsync(27);
        store.Setup(port => port.SearchAsync(
                "approved",
                12,
                "warehouse",
                18,
                startDate,
                endDate,
                3,
                5,
                "supplier",
                "asc",
                27,
                cancellation.Token))
            .ReturnsAsync(expected);
        var handler = new SearchPurchaseOrdersQueryHandler(store.Object, tenantScope.Object);

        var result = await handler.Handle(
            new SearchPurchaseOrdersQuery(
                "approved", 12, 18, startDate, endDate, 3, 5, "supplier", "asc"),
            cancellation.Token);

        Assert.Same(expected, result);
        store.Verify(port => port.SearchAsync(
            "approved",
            12,
            "warehouse",
            18,
            startDate,
            endDate,
            3,
            5,
            "supplier",
            "asc",
            27,
            cancellation.Token), Times.Once);
    }
}
