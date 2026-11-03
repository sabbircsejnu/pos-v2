using Moq;
using RetailPOS.Application.PurchaseOrders.Abstractions;
using RetailPOS.Application.PurchaseOrders.Commands;
using RetailPOS.Application.PurchaseOrders.ReadModels;
using RetailPOS.Core.Entities;

namespace RetailPOS.UnitTests.PurchaseOrders;

public sealed class UpdatePurchaseOrderCommandHandlerTests
{
    [Fact]
    public async Task Handle_ReplacesItemsAndPreservesUpdateProjection()
    {
        var updateStore = new Mock<IPurchaseOrderUpdateStore>();
        var readStore = new Mock<IPurchaseOrderReadStore>();
        var tenantScope = new Mock<IPurchaseOrderTenantScope>();
        var locationStore = new Mock<IPurchaseOrderCreationStore>();
        var locationAuthorizer = new Mock<IPurchaseOrderLocationAuthorizer>();
        var existing = new PurchaseOrder
        {
            Id = 42,
            Status = "draft",
            Items = [new PurchaseOrderItem { Id = 1, VariantId = 1301, Quantity = 1 }]
        };
        var orderDate = new DateTime(2026, 6, 15);
        var expected = new PurchaseOrderReadModel(
            42,
            "PO-20260612-0042",
            1001,
            "Acme Supplier",
            1101,
            "Central Warehouse",
            DateTime.SpecifyKind(orderDate, DateTimeKind.Utc),
            null,
            40m,
            "draft",
            "Revised draft",
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
        locationStore.Setup(store => store.FindLocationAsync(
                new PurchaseOrderDestination("warehouse", 1101),
                27,
                cancellation.Token))
            .ReturnsAsync(new PurchaseOrderLocationScope(27, "Central Warehouse"));
        updateStore.Setup(store => store.FindForUpdateAsync(42, 27, cancellation.Token))
            .ReturnsAsync(existing);
        updateStore.Setup(store => store.SaveUpdateAsync(
                It.Is<PurchaseOrder>(order =>
                    order.SupplierId == 1001 &&
                    order.WarehouseId == 1101 &&
                    order.OrderDate.Kind == DateTimeKind.Utc &&
                    order.TotalAmount == 40m &&
                    order.Notes == "Revised draft" &&
                    order.Items.Count == 1 &&
                    order.Items.Single().VariantId == 1302 &&
                    order.Items.Single().Quantity == 4),
                cancellation.Token))
            .Returns(Task.CompletedTask);
        readStore.Setup(store => store.FindByIdAsync(42, 27, cancellation.Token))
            .ReturnsAsync(expected);
        var handler = new UpdatePurchaseOrderCommandHandler(
            updateStore.Object,
            readStore.Object,
            tenantScope.Object,
            locationStore.Object,
            locationAuthorizer.Object);

        var result = await handler.Handle(
            new UpdatePurchaseOrderCommand(
                42,
                1001,
                new PurchaseOrderDestination("warehouse", 1101),
                orderDate,
                null,
                "Revised draft",
                [new UpdatePurchaseOrderItemCommand(1302, 4, 10m, 0m, 0m, "box")]),
            cancellation.Token);

        Assert.Same(expected, result);
        updateStore.Verify(store => store.SaveUpdateAsync(
            It.IsAny<PurchaseOrder>(),
            cancellation.Token), Times.Once);
        readStore.Verify(store => store.FindByIdAsync(42, 27, cancellation.Token), Times.Once);
        locationAuthorizer.Verify(authorizer => authorizer.AuthorizeAsync(
            "warehouse",
            1101,
            cancellation.Token), Times.Once);
    }
}
