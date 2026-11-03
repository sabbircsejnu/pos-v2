using RetailPOS.Application.Pos.Abstractions;
using RetailPOS.Application.Pos.Queries;
using RetailPOS.Application.Pos.ReadModels;

namespace RetailPOS.Tests.Pos;

public sealed class GetPosStockHintQueryHandlerTests
{
    [Fact]
    public async Task Handle_ReadsOutletStockAndCachesItOnCacheMiss()
    {
        var store = new FakeStore { Quantity = 8 };
        var cache = new FakeCache();
        var handler = new GetPosStockHintQueryHandler(store, cache);
        using var cancellation = new CancellationTokenSource();

        var result = await handler.Handle(
            new GetPosStockHintQuery(12, 34, 8),
            cancellation.Token);

        Assert.Equal(new PosStockHintReadModel(12, 34, 8, true, false), result);
        Assert.Equal(1, store.StockCalls);
        Assert.Equal(12, store.VariantId);
        Assert.Equal(34, store.OutletId);
        Assert.Equal(8, cache.SavedQuantity);
        Assert.Equal(cancellation.Token, store.CancellationToken);
        Assert.Equal(cancellation.Token, cache.CancellationToken);
    }

    [Fact]
    public async Task Handle_UsesCachedOutletStockAndProjectsRequestedQuantity()
    {
        var store = new FakeStore();
        var cache = new FakeCache { CachedQuantity = 8 };
        var handler = new GetPosStockHintQueryHandler(store, cache);
        using var cancellation = new CancellationTokenSource();

        var result = await handler.Handle(
            new GetPosStockHintQuery(12, 34, 9),
            cancellation.Token);

        Assert.Equal(new PosStockHintReadModel(12, 34, 8, false, true), result);
        Assert.Equal(0, store.StockCalls);
        Assert.Equal(cancellation.Token, cache.CancellationToken);
    }

    private sealed class FakeStore : IPosProductLookupStore
    {
        public int Quantity { get; init; }
        public int StockCalls { get; private set; }
        public long VariantId { get; private set; }
        public long OutletId { get; private set; }
        public CancellationToken CancellationToken { get; private set; }

        public Task<int> GetOutletStockAsync(
            long variantId,
            long outletId,
            CancellationToken cancellationToken)
        {
            StockCalls++;
            VariantId = variantId;
            OutletId = outletId;
            CancellationToken = cancellationToken;
            return Task.FromResult(Quantity);
        }

        public Task<PosProductLookupVariantReadModel?> FindVariantByBarcodeAsync(
            string barcode,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PosProductLookupVariantReadModel?> FindFirstActiveProductVariantByBarcodeAsync(
            string barcode,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PosProductLookupVariantReadModel?> FindVariantBySkuAsync(
            string sku,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PosProductLookupVariantReadModel?> FindVariantByIdAsync(
            long variantId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeCache : IPosProductLookupCache
    {
        public int? CachedQuantity { get; init; }
        public int? SavedQuantity { get; private set; }
        public CancellationToken CancellationToken { get; private set; }

        public Task<int?> GetStockAsync(
            long variantId,
            long outletId,
            CancellationToken cancellationToken)
        {
            CancellationToken = cancellationToken;
            return Task.FromResult(CachedQuantity);
        }

        public Task SetStockAsync(
            long variantId,
            long outletId,
            int quantity,
            CancellationToken cancellationToken)
        {
            SavedQuantity = quantity;
            CancellationToken = cancellationToken;
            return Task.CompletedTask;
        }

        public Task<PosProductLookupReadModel?> GetLookupAsync(
            PosProductLookupMode mode,
            string query,
            long? outletId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SetLookupAsync(
            PosProductLookupMode mode,
            string query,
            long? outletId,
            PosProductLookupReadModel result,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
