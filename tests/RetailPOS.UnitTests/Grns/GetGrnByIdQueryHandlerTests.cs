using Moq;
using RetailPOS.Application.Grns.Abstractions;
using RetailPOS.Application.Grns.Queries;
using RetailPOS.Application.Grns.ReadModels;
using RetailPOS.Application.Settings.Abstractions;

namespace RetailPOS.Tests.Grns;

public sealed class GetGrnByIdQueryHandlerTests
{
    private static readonly GrnReadScope Scope = new(1, false, [201], [301]);

    private static Mock<IGrnReadScopeProvider> ScopeProvider(CancellationToken cancellationToken)
    {
        var provider = new Mock<IGrnReadScopeProvider>();
        provider.Setup(port => port.GetAsync(cancellationToken)).ReturnsAsync(Scope);
        return provider;
    }

    [Fact]
    public async Task Handle_UsesReadScopeAndReturnsMappedGrn()
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
        store.Setup(port => port.FindByIdAsync(40, Scope, cancellation.Token))
            .ReturnsAsync(expected);
        var provider = ScopeProvider(cancellation.Token);
        var handler = new GetGrnByIdQueryHandler(store.Object, provider.Object);

        var result = await handler.Handle(new GetGrnByIdQuery(40), cancellation.Token);

        Assert.Same(expected, result);
        provider.Verify(port => port.GetAsync(cancellation.Token), Times.Once);
        store.Verify(port => port.FindByIdAsync(40, Scope, cancellation.Token), Times.Once);
    }

    [Fact]
    public async Task Handle_OutOfScopeGrn_IsIndistinguishableFromMissing()
    {
        var store = new Mock<IGrnReadStore>();
        store.Setup(port => port.FindByIdAsync(40, Scope, It.IsAny<CancellationToken>()))
            .ReturnsAsync((GrnReadModel?)null);
        var handler = new GetGrnByIdQueryHandler(store.Object, ScopeProvider(CancellationToken.None).Object);

        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            handler.Handle(new GetGrnByIdQuery(40), CancellationToken.None));

        Assert.Equal("GRN with ID 40 not found", exception.Message);
        store.Verify(port => port.FindByIdAsync(40, Scope, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetGrns_PropagatesFiltersScopeAndCancellation()
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
        store.Setup(port => port.FindAllAsync(20, "FULL", Scope, cancellation.Token))
            .ReturnsAsync(expected);
        var handler = new GetGrnsQueryHandler(store.Object, ScopeProvider(cancellation.Token).Object);

        var result = await handler.Handle(
            new GetGrnsQuery(20, "FULL"),
            cancellation.Token);

        Assert.Same(expected, result);
        store.Verify(port => port.FindAllAsync(20, "FULL", Scope, cancellation.Token), Times.Once);
    }

    [Fact]
    public async Task SearchGrns_PropagatesLegacyFiltersScopeAndCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var expected = new GrnSearchReadModel([], 4, 2, 2);
        var criteria = new GrnSearchCriteria(
            20,
            "FULL",
            new DateTime(2026, 3, 10),
            new DateTime(2026, 3, 12),
            null,
            null,
            null,
            2,
            2);
        var store = new Mock<IGrnReadStore>();
        store.Setup(port => port.SearchAsync(criteria, Scope, cancellation.Token))
            .ReturnsAsync(expected);
        var handler = new SearchGrnsQueryHandler(
            store.Object,
            ScopeProvider(cancellation.Token).Object,
            Mock.Of<ICompanyTimeZoneProvider>());

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
        store.Verify(port => port.SearchAsync(criteria, Scope, cancellation.Token), Times.Once);
    }

    [Fact]
    public async Task PendingGrnPurchaseOrders_PropagatesReadScopeAndCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        IReadOnlyList<PendingGrnPurchaseOrderReadModel> expected = [];
        var store = new Mock<IPendingGrnPurchaseOrderReadStore>();
        store.Setup(port => port.FindPendingAsync(Scope, cancellation.Token))
            .ReturnsAsync(expected);
        var handler = new GetPendingGrnPurchaseOrdersQueryHandler(
            store.Object,
            ScopeProvider(cancellation.Token).Object);

        var result = await handler.Handle(
            new GetPendingGrnPurchaseOrdersQuery(),
            cancellation.Token);

        Assert.Same(expected, result);
        store.Verify(port => port.FindPendingAsync(Scope, cancellation.Token), Times.Once);
    }

    [Fact]
    public async Task GetVariance_UsesReadScopeAndPreservesNotFoundError()
    {
        using var cancellation = new CancellationTokenSource();
        var expected = new GrnVarianceReadModel(40, 20, "full", []);
        var store = new Mock<IGrnReadStore>();
        store.Setup(port => port.FindVarianceByIdAsync(40, Scope, cancellation.Token))
            .ReturnsAsync(expected);
        store.Setup(port => port.FindVarianceByIdAsync(999, Scope, cancellation.Token))
            .ReturnsAsync((GrnVarianceReadModel?)null);
        var handler = new GetGrnVarianceQueryHandler(store.Object, ScopeProvider(cancellation.Token).Object);

        var result = await handler.Handle(new GetGrnVarianceQuery(40), cancellation.Token);

        Assert.Same(expected, result);
        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            handler.Handle(new GetGrnVarianceQuery(999), cancellation.Token));
        Assert.Equal("GRN with ID 999 not found", exception.Message);
        store.Verify(port => port.FindVarianceByIdAsync(40, Scope, cancellation.Token), Times.Once);
        store.Verify(port => port.FindVarianceByIdAsync(999, Scope, cancellation.Token), Times.Once);
    }

    [Fact]
    public async Task GetPoVariance_UsesReadScopeAndPreservesNotFoundError()
    {
        using var cancellation = new CancellationTokenSource();
        var expected = new GrnPoVarianceReadModel(20, "approved", 3, 1, []);
        var store = new Mock<IGrnPoVarianceReadStore>();
        store.Setup(port => port.FindByPurchaseOrderIdAsync(20, Scope, cancellation.Token))
            .ReturnsAsync(expected);
        store.Setup(port => port.FindByPurchaseOrderIdAsync(999, Scope, cancellation.Token))
            .ReturnsAsync((GrnPoVarianceReadModel?)null);
        var handler = new GetGrnPoVarianceQueryHandler(store.Object, ScopeProvider(cancellation.Token).Object);

        var result = await handler.Handle(new GetGrnPoVarianceQuery(20), cancellation.Token);

        Assert.Same(expected, result);
        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            handler.Handle(new GetGrnPoVarianceQuery(999), cancellation.Token));
        Assert.Equal("Purchase order with ID 999 not found", exception.Message);
        store.Verify(port => port.FindByPurchaseOrderIdAsync(20, Scope, cancellation.Token), Times.Once);
        store.Verify(port => port.FindByPurchaseOrderIdAsync(999, Scope, cancellation.Token), Times.Once);
    }
}
