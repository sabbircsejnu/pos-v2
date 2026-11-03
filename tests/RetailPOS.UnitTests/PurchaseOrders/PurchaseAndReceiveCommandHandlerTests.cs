using Moq;
using RetailPOS.Application.PurchaseOrders.Abstractions;
using RetailPOS.Application.PurchaseOrders.Commands;
using RetailPOS.Application.PurchaseOrders.ReadModels;

namespace RetailPOS.UnitTests.PurchaseOrders;

public sealed class PurchaseAndReceiveCommandHandlerTests
{
    [Fact]
    public async Task Handle_TrimsIdempotencyKeyAndReturnsScopedPurchaseOrderProjection()
    {
        var store = new Mock<IPurchaseAndReceiveStore>();
        var readStore = new Mock<IPurchaseOrderReadStore>();
        var tenantScope = new Mock<IPurchaseOrderTenantScope>();
        var cancellationToken = new CancellationTokenSource().Token;
        var purchaseOrder = CreatePurchaseOrderReadModel(41);
        tenantScope.Setup(scope => scope.GetBusinessIdAsync(cancellationToken)).ReturnsAsync(7);
        store.Setup(value => value.ExecuteAsync(
                It.IsAny<PurchaseAndReceiveCommand>(),
                7,
                cancellationToken))
            .ReturnsAsync(new PurchaseAndReceiveStoreResult(41, 52, false));
        readStore.Setup(value => value.FindByIdAsync(41, 7, cancellationToken))
            .ReturnsAsync(purchaseOrder);
        var handler = new PurchaseAndReceiveCommandHandler(store.Object, readStore.Object, tenantScope.Object, Mock.Of<IPurchaseOrderLocationAuthorizer>());

        var result = await handler.Handle(CreateCommand("  request-1  "), cancellationToken);

        Assert.Same(purchaseOrder, result.PurchaseOrder);
        Assert.Equal(52, result.GrnId);
        Assert.False(result.IsDuplicateRequest);
        store.Verify(value => value.ExecuteAsync(
            It.Is<PurchaseAndReceiveCommand>(command => command.IdempotencyKey == "request-1"),
            7,
            cancellationToken), Times.Once);
        readStore.Verify(value => value.FindByIdAsync(41, 7, cancellationToken), Times.Once);
    }

    [Fact]
    public async Task Handle_RejectsMissingIdempotencyKeyBeforeInvokingStore()
    {
        var store = new Mock<IPurchaseAndReceiveStore>();
        var readStore = new Mock<IPurchaseOrderReadStore>();
        var tenantScope = new Mock<IPurchaseOrderTenantScope>();
        tenantScope.Setup(scope => scope.GetBusinessIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync(7);
        var handler = new PurchaseAndReceiveCommandHandler(store.Object, readStore.Object, tenantScope.Object, Mock.Of<IPurchaseOrderLocationAuthorizer>());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.Handle(CreateCommand("  "), CancellationToken.None));

        Assert.Equal("Idempotency key is required for purchase and receive action", exception.Message);
        store.Verify(value => value.ExecuteAsync(
            It.IsAny<PurchaseAndReceiveCommand>(),
            It.IsAny<long?>(),
            It.IsAny<CancellationToken>()), Times.Never);
        readStore.Verify(value => value.FindByIdAsync(
            It.IsAny<long>(),
            It.IsAny<long?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ResolvesExistingDuplicateBeforeItemValidationAndReturnsDuplicateFlag()
    {
        var store = new Mock<IPurchaseAndReceiveStore>();
        var readStore = new Mock<IPurchaseOrderReadStore>();
        var tenantScope = new Mock<IPurchaseOrderTenantScope>();
        var purchaseOrder = CreatePurchaseOrderReadModel(41);
        tenantScope.Setup(scope => scope.GetBusinessIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync(7);
        store.Setup(value => value.ExecuteAsync(
                It.IsAny<PurchaseAndReceiveCommand>(),
                7,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PurchaseAndReceiveStoreResult(41, 52, true));
        readStore.Setup(value => value.FindByIdAsync(
                41,
                7,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(purchaseOrder);
        var handler = new PurchaseAndReceiveCommandHandler(store.Object, readStore.Object, tenantScope.Object, Mock.Of<IPurchaseOrderLocationAuthorizer>());

        var result = await handler.Handle(CreateCommand("request-1", items: null), CancellationToken.None);

        Assert.Same(purchaseOrder, result.PurchaseOrder);
        Assert.Equal(52, result.GrnId);
        Assert.True(result.IsDuplicateRequest);
    }

    [Fact]
    public async Task Handle_OutletDestinationDeniedBeforeAnyStockWrite()
    {
        var store = new Mock<IPurchaseAndReceiveStore>();
        var tenantScope = new Mock<IPurchaseOrderTenantScope>();
        tenantScope.Setup(scope => scope.GetBusinessIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync(7);
        var authorizer = new Mock<IPurchaseOrderLocationAuthorizer>();
        authorizer.Setup(value => value.AuthorizeAsync("outlet", 15, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException("Outlet access denied."));
        var handler = new PurchaseAndReceiveCommandHandler(
            store.Object,
            Mock.Of<IPurchaseOrderReadStore>(),
            tenantScope.Object,
            authorizer.Object);

        var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handler.Handle(
            CreateCommand("request-2", destination: new PurchaseOrderDestination("outlet", 15)),
            CancellationToken.None));

        Assert.Equal("Outlet access denied.", exception.Message);
        store.Verify(value => value.ExecuteAsync(
            It.IsAny<PurchaseAndReceiveCommand>(),
            It.IsAny<long?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_OutletDestinationIsAuthorizedAndPassedToStore()
    {
        var store = new Mock<IPurchaseAndReceiveStore>();
        var readStore = new Mock<IPurchaseOrderReadStore>();
        var tenantScope = new Mock<IPurchaseOrderTenantScope>();
        var authorizer = new Mock<IPurchaseOrderLocationAuthorizer>();
        tenantScope.Setup(scope => scope.GetBusinessIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync(7);
        store.Setup(value => value.ExecuteAsync(
                It.IsAny<PurchaseAndReceiveCommand>(),
                7,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PurchaseAndReceiveStoreResult(41, 52, false));
        readStore.Setup(value => value.FindByIdAsync(41, 7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreatePurchaseOrderReadModel(41));
        var handler = new PurchaseAndReceiveCommandHandler(
            store.Object,
            readStore.Object,
            tenantScope.Object,
            authorizer.Object);

        await handler.Handle(
            CreateCommand("request-3", destination: new PurchaseOrderDestination("outlet", 15)),
            CancellationToken.None);

        authorizer.Verify(value => value.AuthorizeAsync("outlet", 15, It.IsAny<CancellationToken>()), Times.Once);
        store.Verify(value => value.ExecuteAsync(
            It.Is<PurchaseAndReceiveCommand>(command =>
                command.Destination.LocationType == "outlet"
                && command.Destination.LocationId == 15),
            7,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private static PurchaseAndReceiveCommand CreateCommand(        string? idempotencyKey,
        IReadOnlyList<PurchaseAndReceiveItemCommand>? items = null,
        PurchaseOrderDestination? destination = null) =>
        new(
            20,
            destination ?? new PurchaseOrderDestination("warehouse", 10),
            new DateTime(2026, 10, 4, 0, 0, 0, DateTimeKind.Utc),
            null,
            "immediate",
            items,
            idempotencyKey,
            77);

    private static PurchaseOrderReadModel CreatePurchaseOrderReadModel(long id) =>
        new(
            id,
            "PO-20261004-0041",
            20,
            "Supplier",
            10,
            "Warehouse",
            new DateTime(2026, 10, 4, 0, 0, 0, DateTimeKind.Utc),
            null,
            37.5m,
            "completed",
            "immediate",
            null,
            77,
            "User",
            DateTime.UtcNow,
            DateTime.UtcNow,
            52,
            [new PurchaseOrderItemReadModel(1, 30, "Product", "Variant", "P", "SKU", null, 3, "pcs", 12.5m, 10m, 5m)]);
}
