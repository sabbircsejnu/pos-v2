using Moq;
using RetailPOS.Application.PurchaseOrders.Abstractions;
using RetailPOS.Application.PurchaseOrders.Commands;
using RetailPOS.Application.PurchaseOrders.ReadModels;
using RetailPOS.Core.Entities;

namespace RetailPOS.UnitTests.PurchaseOrders;

public sealed class CreatePurchaseOrderCommandHandlerTests
{
    [Fact]
    public async Task Handle_ValidatesScopesRoundsCreatesNumbersAndReadsBackProjection()
    {
        var creationStore = new Mock<IPurchaseOrderCreationStore>();
        var readStore = new Mock<IPurchaseOrderReadStore>();
        var tenantScope = new Mock<IPurchaseOrderTenantScope>();
        var locationAuthorizer = new Mock<IPurchaseOrderLocationAuthorizer>();
        var orderDate = new DateTime(2026, 6, 13);
        var expected = new PurchaseOrderReadModel(
            42,
            "PO-20260613-0042",
            1001,
            "Acme Supplier",
            1101,
            "Central Warehouse",
            DateTime.SpecifyKind(orderDate, DateTimeKind.Utc),
            null,
            75.44m,
            "draft",
            "PO notes",
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
        creationStore.Setup(store => store.FindLocationAsync(
                new PurchaseOrderDestination("warehouse", 1101),
                27,
                cancellation.Token))
            .ReturnsAsync(new PurchaseOrderLocationScope(27, "Central Warehouse"));
        creationStore.Setup(store => store.SupplierExistsAsync(1001, 27, cancellation.Token))
            .ReturnsAsync(true);
        creationStore.Setup(store => store.ProductVariantExistsAsync(1301, cancellation.Token))
            .ReturnsAsync(true);
        creationStore.Setup(store => store.ProductVariantExistsAsync(1302, cancellation.Token))
            .ReturnsAsync(true);
        creationStore.Setup(store => store.CreateAsync(
                It.Is<PurchaseOrder>(order =>
                    order.SupplierId == 1001 &&
                    order.WarehouseId == 1101 &&
                    order.OutletId == null &&
                    order.OrderDate == DateTime.SpecifyKind(orderDate, DateTimeKind.Utc) &&
                    order.OrderDate.Kind == DateTimeKind.Utc &&
                    order.Status == "draft" &&
                    order.Notes == "PO notes" &&
                    order.TotalAmount == 75.44m &&
                    order.CreatedBy == 321 &&
                    order.Items.Count == 2 &&
                    order.Items.First().Quantity == 3),
                cancellation.Token))
            .ReturnsAsync(42);
        creationStore.Setup(store => store.SetPoNumberAsync(
                42,
                "PO-20260613-0042",
                cancellation.Token))
            .Returns(Task.CompletedTask);
        readStore.Setup(store => store.FindByIdAsync(42, 27, cancellation.Token))
            .ReturnsAsync(expected);
        var handler = new CreatePurchaseOrderCommandHandler(
            creationStore.Object,
            readStore.Object,
            tenantScope.Object,
            locationAuthorizer.Object);

        var result = await handler.Handle(
            new CreatePurchaseOrderCommand(
                1001,
                new PurchaseOrderDestination("warehouse", 1101),
                orderDate,
                null,
                "DRAFT",
                "PO notes",
                [
                    new CreatePurchaseOrderItemCommand(1301, 3, 12.50m, 10m, 5m, "pcs"),
                    new CreatePurchaseOrderItemCommand(1302, 2, 20m, 0m, 0m, "pcs")
                ],
                321),
            cancellation.Token);

        Assert.Same(expected, result);
        creationStore.Verify(store => store.CreateAsync(
            It.IsAny<PurchaseOrder>(),
            cancellation.Token), Times.Once);
        creationStore.Verify(store => store.SetPoNumberAsync(
            42,
            "PO-20260613-0042",
            cancellation.Token), Times.Once);
        readStore.Verify(store => store.FindByIdAsync(42, 27, cancellation.Token), Times.Once);
        locationAuthorizer.Verify(authorizer => authorizer.AuthorizeAsync(
            "warehouse",
            1101,
            cancellation.Token), Times.Once);
    }

    [Fact]
    public async Task Handle_RejectsEmptyItemsBeforeAnyPersistence()
    {
        var creationStore = new Mock<IPurchaseOrderCreationStore>();
        var tenantScope = new Mock<IPurchaseOrderTenantScope>();
        tenantScope.Setup(scope => scope.GetBusinessIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(27);
        var handler = new CreatePurchaseOrderCommandHandler(
            creationStore.Object,
            Mock.Of<IPurchaseOrderReadStore>(),
            tenantScope.Object,
            Mock.Of<IPurchaseOrderLocationAuthorizer>());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(
                new CreatePurchaseOrderCommand(
                    1001, new PurchaseOrderDestination("warehouse", 1101), DateTime.UtcNow, null, "draft", null, [], 321),
                CancellationToken.None));

        Assert.Equal("Purchase order must have at least one item", exception.Message);
        creationStore.VerifyNoOtherCalls();
    }
}
