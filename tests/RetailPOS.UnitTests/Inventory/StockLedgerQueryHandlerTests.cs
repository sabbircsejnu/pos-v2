using Moq;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.Queries;
using RetailPOS.Application.Inventory.StockLedger;

namespace RetailPOS.UnitTests.Inventory;

public sealed class StockLedgerQueryHandlerTests
{
    [Fact]
    public async Task SearchQuery_UsesTrustedScopeAndForwardsCancellation()
    {
        var expectedScope = new StockLedgerReadScope(
            4101,
            [new StockLedgerLocationScope(5101, "outlet")]);
        var scopeProvider = new Mock<IStockLedgerReadScopeProvider>(MockBehavior.Strict);
        var readStore = new Mock<IStockLedgerReadStore>(MockBehavior.Strict);
        var filter = new StockLedgerSearchFilter(
            6101,
            null,
            null,
            "sale",
            null,
            null,
            null,
            null,
            1,
            20);
        var expectedResult = new StockLedgerSearchReadModel(
            Array.Empty<StockLedgerReadModel>(),
            0,
            1,
            20,
            0,
            false,
            false);
        using var cancellation = new CancellationTokenSource();
        scopeProvider
            .Setup(provider => provider.GetAsync(cancellation.Token))
            .ReturnsAsync(expectedScope);
        readStore
            .Setup(store => store.SearchAsync(filter, expectedScope, cancellation.Token))
            .ReturnsAsync(expectedResult);

        var handler = new SearchStockLedgerQueryHandler(readStore.Object, scopeProvider.Object);
        var result = await handler.Handle(new SearchStockLedgerQuery(filter), cancellation.Token);

        Assert.Same(expectedResult, result);
        scopeProvider.Verify(provider => provider.GetAsync(cancellation.Token), Times.Once);
        readStore.Verify(store => store.SearchAsync(filter, expectedScope, cancellation.Token), Times.Once);
    }
}
