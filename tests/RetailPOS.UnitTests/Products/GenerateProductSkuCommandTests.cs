using RetailPOS.Application.Products.Abstractions;
using RetailPOS.Application.Products.Commands;

namespace RetailPOS.Tests.Products;

public sealed class GenerateProductSkuCommandHandlerTests
{
    [Fact]
    public async Task Handle_PadsShortPrefixAndIncrementsCollisionSuffix()
    {
        var store = new FakeProductSkuStore(collisions: 2);
        var handler = new GenerateProductSkuCommandHandler(store);

        var sku = await handler.Handle(
            new GenerateProductSkuCommand("a"),
            CancellationToken.None);

        Assert.Matches("^AXX-[0-9]{14}-2$", sku);
        Assert.Equal(3, store.LookupCount);
    }

    [Fact]
    public async Task Handle_UsesFallbackPrefixWhenNameHasNoLettersOrDigits()
    {
        var handler = new GenerateProductSkuCommandHandler(new FakeProductSkuStore(collisions: 0));

        var sku = await handler.Handle(
            new GenerateProductSkuCommand(" !!! "),
            CancellationToken.None);

        Assert.Matches("^PRD-[0-9]{14}$", sku);
    }

    private sealed class FakeProductSkuStore(int collisions) : IProductSkuStore
    {
        public int LookupCount { get; private set; }

        public Task<bool> SkuExistsAsync(string sku, CancellationToken cancellationToken)
        {
            LookupCount++;
            return Task.FromResult(LookupCount <= collisions);
        }
    }
}
