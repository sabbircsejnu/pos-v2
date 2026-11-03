using Moq;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.Queries;
using RetailPOS.Application.Inventory.StockAdjustments;

namespace RetailPOS.UnitTests.Inventory;

public sealed class StockAdjustmentQueryHandlerTests
{
    [Fact]
    public async Task SearchQuery_UsesTrustedScopeAndForwardsCancellation()
    {
        var expectedScope = new StockAdjustmentReadScope(
            4101,
            [new StockAdjustmentLocationScope(5101, "outlet")]);
        var scopeProvider = new Mock<IStockAdjustmentReadScopeProvider>(MockBehavior.Strict);
        var readStore = new Mock<IStockAdjustmentReadStore>(MockBehavior.Strict);
        var filter = new StockAdjustmentSearchFilter(
            5101,
            "outlet",
            6101,
            "Approved",
            new DateTime(2026, 10, 1),
            new DateTime(2026, 10, 31),
            1,
            20);
        var expectedResult = new StockAdjustmentSearchReadModel(
            Array.Empty<StockAdjustmentReadModel>(),
            0,
            1,
            20);
        using var cancellation = new CancellationTokenSource();
        scopeProvider
            .Setup(provider => provider.GetAsync(cancellation.Token))
            .ReturnsAsync(expectedScope);
        readStore
            .Setup(store => store.SearchAsync(filter, expectedScope, cancellation.Token))
            .ReturnsAsync(expectedResult);

        var handler = new SearchStockAdjustmentsQueryHandler(readStore.Object, scopeProvider.Object);
        var result = await handler.Handle(new SearchStockAdjustmentsQuery(filter), cancellation.Token);

        Assert.Same(expectedResult, result);
        scopeProvider.Verify(provider => provider.GetAsync(cancellation.Token), Times.Once);
        readStore.Verify(store => store.SearchAsync(filter, expectedScope, cancellation.Token), Times.Once);
    }
}
