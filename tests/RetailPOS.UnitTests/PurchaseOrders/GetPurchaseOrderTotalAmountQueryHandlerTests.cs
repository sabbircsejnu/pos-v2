using Moq;
using RetailPOS.Application.PurchaseOrders.Abstractions;
using RetailPOS.Application.PurchaseOrders.Queries;

namespace RetailPOS.UnitTests.PurchaseOrders;

public sealed class GetPurchaseOrderTotalAmountQueryHandlerTests
{
    [Fact]
    public async Task Handle_ForwardsFiltersAndAuthoritativeBusinessScope()
    {
        var store = new Mock<IPurchaseOrderReadStore>();
        var tenantScope = new Mock<IPurchaseOrderTenantScope>();
        var startDate = new DateTime(2026, 6, 1);
        var endDate = new DateTime(2026, 6, 30);
        using var cancellation = new CancellationTokenSource();
        tenantScope.Setup(scope => scope.GetBusinessIdAsync(cancellation.Token))
            .ReturnsAsync(27);
        store.Setup(port => port.SumTotalAmountAsync(
                "draft",
                startDate,
                endDate,
                27,
                cancellation.Token))
            .ReturnsAsync(321.45m);
        var handler = new GetPurchaseOrderTotalAmountQueryHandler(
            store.Object,
            tenantScope.Object);

        var result = await handler.Handle(
            new GetPurchaseOrderTotalAmountQuery("draft", startDate, endDate),
            cancellation.Token);

        Assert.Equal(321.45m, result);
        store.Verify(port => port.SumTotalAmountAsync(
            "draft",
            startDate,
            endDate,
            27,
            cancellation.Token), Times.Once);
    }
}
