using Moq;
using RetailPOS.Application.Grns.Abstractions;
using RetailPOS.Application.Grns.Queries;
using RetailPOS.Application.Grns.ReadModels;

namespace RetailPOS.Tests.Grns;

public sealed class GetGrnByIdQueryHandlerTests
{
    [Fact]
    public async Task Handle_UsesBusinessScopeAndReturnsMappedGrn()
    {
        using var cancellation = new CancellationTokenSource();
        var expected = new GrnReadModel(
            40,
            20,
            10,
            "Supplier",
            30,
            "Warehouse",
            new DateTime(2026, 3, 10),
            "completed",
            "Dock receipt",
            "INV-40",
            new DateTime(2026, 3, 10),
            77,
            "Receiver",
            new DateTime(2026, 3, 10),
            []);
        var store = new Mock<IGrnReadStore>();
        store.Setup(port => port.FindByIdAsync(40, 1, cancellation.Token))
            .ReturnsAsync(expected);
        var tenantScope = new Mock<IGrnTenantScope>();
        tenantScope.Setup(port => port.GetBusinessIdAsync(cancellation.Token))
            .ReturnsAsync(1);
        var handler = new GetGrnByIdQueryHandler(store.Object, tenantScope.Object);

        var result = await handler.Handle(new GetGrnByIdQuery(40), cancellation.Token);

        Assert.Same(expected, result);
        tenantScope.Verify(port => port.GetBusinessIdAsync(cancellation.Token), Times.Once);
        store.Verify(port => port.FindByIdAsync(40, 1, cancellation.Token), Times.Once);
    }

    [Fact]
    public async Task Handle_ReturnsLegacyNotFoundErrorForMissingOrOtherBusinessGrn()
    {
        var store = new Mock<IGrnReadStore>();
        store.Setup(port => port.FindByIdAsync(40, 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync((GrnReadModel?)null);
        var tenantScope = new Mock<IGrnTenantScope>();
        tenantScope.Setup(port => port.GetBusinessIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);
        var handler = new GetGrnByIdQueryHandler(store.Object, tenantScope.Object);

        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            handler.Handle(new GetGrnByIdQuery(40), CancellationToken.None));

        Assert.Equal("GRN with ID 40 not found", exception.Message);
        store.Verify(port => port.FindByIdAsync(40, 2, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetGrns_PropagatesFiltersBusinessAndCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var expected = new[]
        {
            new GrnReadModel(
                40,
                20,
                10,
                "Supplier",
                30,
                "Warehouse",
                new DateTime(2026, 3, 10),
                "full",
                null,
                null,
                null,
                null,
                null,
                new DateTime(2026, 3, 10),
                [])
        };
        var store = new Mock<IGrnReadStore>();
        store.Setup(port => port.FindAllAsync(20, "FULL", 1, cancellation.Token))
            .ReturnsAsync(expected);
        var tenantScope = new Mock<IGrnTenantScope>();
        tenantScope.Setup(port => port.GetBusinessIdAsync(cancellation.Token))
            .ReturnsAsync(1);
        var handler = new GetGrnsQueryHandler(store.Object, tenantScope.Object);

        var result = await handler.Handle(
            new GetGrnsQuery(20, "FULL"),
            cancellation.Token);

        Assert.Same(expected, result);
        store.Verify(port => port.FindAllAsync(20, "FULL", 1, cancellation.Token), Times.Once);
    }

    [Fact]
    public async Task SearchGrns_PropagatesFiltersBusinessAndCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var expected = new GrnSearchReadModel([], 4, 2, 2);
        var store = new Mock<IGrnReadStore>();
        store.Setup(port => port.SearchAsync(
                20,
                "FULL",
                new DateTime(2026, 3, 10),
                new DateTime(2026, 3, 12),
                2,
                2,
                1,
                cancellation.Token))
            .ReturnsAsync(expected);
        var tenantScope = new Mock<IGrnTenantScope>();
        tenantScope.Setup(port => port.GetBusinessIdAsync(cancellation.Token))
            .ReturnsAsync(1);
        var handler = new SearchGrnsQueryHandler(store.Object, tenantScope.Object);

        var result = await handler.Handle(
            new SearchGrnsQuery(
                20,
                "FULL",
                new DateTime(2026, 3, 10),
                new DateTime(2026, 3, 12),
                2,
                2),
            cancellation.Token);

        Assert.Same(expected, result);
        store.Verify(port => port.SearchAsync(
            20,
            "FULL",
            new DateTime(2026, 3, 10),
            new DateTime(2026, 3, 12),
            2,
            2,
            1,
            cancellation.Token), Times.Once);
    }

    [Fact]
    public async Task PendingGrnPurchaseOrders_PropagatesBusinessAndCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        IReadOnlyList<PendingGrnPurchaseOrderReadModel> expected = [];
        var store = new Mock<IPendingGrnPurchaseOrderReadStore>();
        store.Setup(port => port.FindPendingAsync(1, cancellation.Token))
            .ReturnsAsync(expected);
        var tenantScope = new Mock<IGrnTenantScope>();
        tenantScope.Setup(port => port.GetBusinessIdAsync(cancellation.Token))
            .ReturnsAsync(1);
        var handler = new GetPendingGrnPurchaseOrdersQueryHandler(store.Object, tenantScope.Object);

        var result = await handler.Handle(
            new GetPendingGrnPurchaseOrdersQuery(),
            cancellation.Token);

        Assert.Same(expected, result);
        store.Verify(port => port.FindPendingAsync(1, cancellation.Token), Times.Once);
    }

    [Fact]
    public async Task GetVariance_UsesBusinessScopeAndPreservesNotFoundError()
    {
        using var cancellation = new CancellationTokenSource();
        var expected = new GrnVarianceReadModel(40, 20, "full", []);
        var store = new Mock<IGrnReadStore>();
        store.Setup(port => port.FindVarianceByIdAsync(40, 1, cancellation.Token))
            .ReturnsAsync(expected);
        store.Setup(port => port.FindVarianceByIdAsync(999, 1, cancellation.Token))
            .ReturnsAsync((GrnVarianceReadModel?)null);
        var tenantScope = new Mock<IGrnTenantScope>();
        tenantScope.Setup(port => port.GetBusinessIdAsync(cancellation.Token))
            .ReturnsAsync(1);
        var handler = new GetGrnVarianceQueryHandler(store.Object, tenantScope.Object);

        var result = await handler.Handle(new GetGrnVarianceQuery(40), cancellation.Token);

        Assert.Same(expected, result);
        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            handler.Handle(new GetGrnVarianceQuery(999), cancellation.Token));
        Assert.Equal("GRN with ID 999 not found", exception.Message);
        store.Verify(port => port.FindVarianceByIdAsync(40, 1, cancellation.Token), Times.Once);
        store.Verify(port => port.FindVarianceByIdAsync(999, 1, cancellation.Token), Times.Once);
    }

    [Fact]
    public async Task GetPoVariance_UsesBusinessScopeAndPreservesNotFoundError()
    {
        using var cancellation = new CancellationTokenSource();
        var expected = new GrnPoVarianceReadModel(20, "approved", 3, 1, []);
        var store = new Mock<IGrnPoVarianceReadStore>();
        store.Setup(port => port.FindByPurchaseOrderIdAsync(20, 1, cancellation.Token))
            .ReturnsAsync(expected);
        store.Setup(port => port.FindByPurchaseOrderIdAsync(999, 1, cancellation.Token))
            .ReturnsAsync((GrnPoVarianceReadModel?)null);
        var tenantScope = new Mock<IGrnTenantScope>();
        tenantScope.Setup(port => port.GetBusinessIdAsync(cancellation.Token))
            .ReturnsAsync(1);
        var handler = new GetGrnPoVarianceQueryHandler(store.Object, tenantScope.Object);

        var result = await handler.Handle(new GetGrnPoVarianceQuery(20), cancellation.Token);

        Assert.Same(expected, result);
        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            handler.Handle(new GetGrnPoVarianceQuery(999), cancellation.Token));
        Assert.Equal("Purchase order with ID 999 not found", exception.Message);
        store.Verify(port => port.FindByPurchaseOrderIdAsync(20, 1, cancellation.Token), Times.Once);
        store.Verify(port => port.FindByPurchaseOrderIdAsync(999, 1, cancellation.Token), Times.Once);
    }
}
