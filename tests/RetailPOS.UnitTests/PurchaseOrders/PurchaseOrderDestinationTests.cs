using Moq;
using RetailPOS.Application.PurchaseOrders.Abstractions;
using RetailPOS.Application.PurchaseOrders.Commands;
using RetailPOS.Application.PurchaseOrders.ReadModels;
using RetailPOS.Core.Entities;

namespace RetailPOS.UnitTests.PurchaseOrders;

public sealed class PurchaseOrderDestinationTests
{
    [Fact]
    public void Resolve_LegacyWarehouseIdStillMapsToWarehouseDestination()
    {
        var destination = PurchaseOrderDestination.Resolve(null, null, 1101);

        Assert.Equal(new PurchaseOrderDestination("warehouse", 1101), destination);
    }

    [Theory]
    [InlineData("outlet", "outlet")]
    [InlineData("OUTLET", "outlet")]
    [InlineData(" Warehouse ", "warehouse")]
    public void Resolve_LocationPairTakesPrecedenceOverLegacyWarehouseId(string type, string expectedType)
    {
        var destination = PurchaseOrderDestination.Resolve(type, 15, 1101);

        Assert.Equal(new PurchaseOrderDestination(expectedType, 15), destination);
    }

    [Fact]
    public void Resolve_OutletTypeWithoutLocationIdIsRejected()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => PurchaseOrderDestination.Resolve("outlet", null, 1101));

        Assert.Equal("A destination outlet is required.", exception.Message);
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("warehouse", null, null)]
    [InlineData(null, 0L, 0L)]
    public void Resolve_MissingDestinationIsRejected(string? type, long? locationId, long? warehouseId)
    {
        Assert.Throws<InvalidOperationException>(
            () => PurchaseOrderDestination.Resolve(type, locationId, warehouseId));
    }

    [Fact]
    public void Resolve_UnknownLocationTypeIsRejected()
    {
        Assert.Throws<InvalidOperationException>(
            () => PurchaseOrderDestination.Resolve("store", 15, null));
    }

    [Fact]
    public void SetDestination_KeepsExactlyOneDestination()
    {
        var order = new PurchaseOrder { WarehouseId = 1101 };

        order.SetDestination("outlet", 15);

        Assert.Null(order.WarehouseId);
        Assert.Equal(15, order.OutletId);
        Assert.Equal("outlet", order.LocationType);
        Assert.Equal(15, order.LocationId);

        order.SetDestination("warehouse", 1101);

        Assert.Null(order.OutletId);
        Assert.Equal(1101, order.WarehouseId);
        Assert.Equal("warehouse", order.LocationType);
        Assert.Equal(1101, order.LocationId);
    }

    [Fact]
    public void ReadModel_ExposesOutletAsLocationForOutletOnlyPurchases()
    {
        var model = new PurchaseOrderReadModel(
            1, "PO-1", 2, "Supplier", null, string.Empty, DateTime.UtcNow, null, 0m, "draft",
            null, null, null, null, DateTime.UtcNow, DateTime.UtcNow, null, [], 15, "Main Outlet");

        Assert.Equal("outlet", model.LocationType);
        Assert.Equal(15, model.LocationId);
        Assert.Equal("Main Outlet", model.LocationName);
    }

    [Fact]
    public async Task CreateHandler_OutletDestinationIsValidatedAuthorizedAndPersistedWithoutWarehouse()
    {
        var creationStore = new Mock<IPurchaseOrderCreationStore>();
        var readStore = new Mock<IPurchaseOrderReadStore>();
        var tenantScope = new Mock<IPurchaseOrderTenantScope>();
        var authorizer = new Mock<IPurchaseOrderLocationAuthorizer>();
        var destination = new PurchaseOrderDestination("outlet", 15);
        tenantScope.Setup(scope => scope.GetBusinessIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync(27);
        creationStore.Setup(store => store.FindLocationAsync(destination, 27, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PurchaseOrderLocationScope(27, "Main Outlet"));
        creationStore.Setup(store => store.SupplierExistsAsync(1001, 27, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        creationStore.Setup(store => store.ProductVariantExistsAsync(1301, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        creationStore.Setup(store => store.CreateAsync(It.IsAny<PurchaseOrder>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(42);
        readStore.Setup(store => store.FindByIdAsync(42, 27, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PurchaseOrderReadModel(
                42, "PO-1", 1001, "Supplier", null, string.Empty, DateTime.UtcNow, null, 10m, "draft",
                null, null, null, null, DateTime.UtcNow, DateTime.UtcNow, null, [], 15, "Main Outlet"));
        var handler = new CreatePurchaseOrderCommandHandler(
            creationStore.Object,
            readStore.Object,
            tenantScope.Object,
            authorizer.Object);

        var result = await handler.Handle(
            new CreatePurchaseOrderCommand(
                1001,
                destination,
                DateTime.UtcNow,
                null,
                "draft",
                null,
                [new CreatePurchaseOrderItemCommand(1301, 1, 10m, 0m, 0m, "pcs")],
                321),
            CancellationToken.None);

        Assert.Equal("outlet", result.LocationType);
        authorizer.Verify(value => value.AuthorizeAsync("outlet", 15, It.IsAny<CancellationToken>()), Times.Once);
        creationStore.Verify(store => store.CreateAsync(
            It.Is<PurchaseOrder>(order => order.OutletId == 15 && order.WarehouseId == null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateHandler_LocationOutsideBusinessIsRejectedBeforeAuthorizationOrWrite()
    {
        var creationStore = new Mock<IPurchaseOrderCreationStore>();
        var tenantScope = new Mock<IPurchaseOrderTenantScope>();
        var authorizer = new Mock<IPurchaseOrderLocationAuthorizer>();
        tenantScope.Setup(scope => scope.GetBusinessIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync(27);
        creationStore.Setup(store => store.FindLocationAsync(
                It.IsAny<PurchaseOrderDestination>(),
                27,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((PurchaseOrderLocationScope?)null);
        var handler = new CreatePurchaseOrderCommandHandler(
            creationStore.Object,
            Mock.Of<IPurchaseOrderReadStore>(),
            tenantScope.Object,
            authorizer.Object);

        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(() => handler.Handle(
            new CreatePurchaseOrderCommand(
                1001,
                new PurchaseOrderDestination("outlet", 99),
                DateTime.UtcNow,
                null,
                "draft",
                null,
                [new CreatePurchaseOrderItemCommand(1301, 1, 10m, 0m, 0m, "pcs")],
                321),
            CancellationToken.None));

        Assert.Equal("Outlet with ID 99 not found", exception.Message);
        authorizer.VerifyNoOtherCalls();
        creationStore.Verify(store => store.CreateAsync(
            It.IsAny<PurchaseOrder>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }
}
