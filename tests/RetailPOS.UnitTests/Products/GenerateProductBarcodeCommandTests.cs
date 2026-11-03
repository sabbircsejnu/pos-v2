using RetailPOS.Application.Products.Abstractions;
using RetailPOS.Application.Products.Commands;

namespace RetailPOS.Tests.Products;

public sealed class GenerateProductBarcodeCommandHandlerTests
{
    [Fact]
    public async Task Handle_RetriesCollisionsAndReturnsValidEan13()
    {
        var store = new FakeProductCreateStore(collisions: 2);
        var handler = new GenerateProductBarcodeCommandHandler(store);

        var barcode = await handler.Handle(
            new GenerateProductBarcodeCommand(2),
            CancellationToken.None);

        Assert.Matches("^880002[0-9]{7}$", barcode);
        Assert.Equal(CalculateCheckDigit(barcode[..12]), barcode[^1] - '0');
        Assert.Equal(3, store.LookupCount);
    }

    [Fact]
    public async Task Handle_FailsExplicitlyWhenEveryCandidateCollides()
    {
        var store = new FakeProductCreateStore(collisions: 100);
        var handler = new GenerateProductBarcodeCommandHandler(store);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new GenerateProductBarcodeCommand(null), CancellationToken.None));

        Assert.Equal("Unable to generate a unique barcode. Please try again.", exception.Message);
        Assert.Equal(100, store.LookupCount);
    }

    private static int CalculateCheckDigit(string base12)
    {
        var sum = 0;
        for (var index = 0; index < base12.Length; index++)
        {
            var digit = base12[index] - '0';
            sum += index % 2 == 0 ? digit : digit * 3;
        }

        return (10 - sum % 10) % 10;
    }

    private sealed class FakeProductCreateStore(int collisions) : IProductCreateStore
    {
        public int LookupCount { get; private set; }

        public Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<bool> SkuExistsAsync(string sku, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<bool> BarcodeExistsAsync(string barcode, CancellationToken cancellationToken)
        {
            LookupCount++;
            return Task.FromResult(LookupCount <= collisions);
        }

        public Task<bool> ProductCodeExistsAsync(string productCode, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<long> CreateAsync(
            RetailPOS.Core.Entities.Product product,
            IReadOnlyList<ProductVariantCreation> variants,
            CancellationToken cancellationToken) =>
            Task.FromResult(1L);
    }
}
