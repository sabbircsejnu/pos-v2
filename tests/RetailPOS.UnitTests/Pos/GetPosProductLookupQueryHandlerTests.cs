using RetailPOS.Application.Pos.Abstractions;
using RetailPOS.Application.Pos.Queries;
using RetailPOS.Application.Pos.ReadModels;
using RetailPOS.Application.Sales.Checkout;

namespace RetailPOS.Tests.Pos;

public sealed class GetPosProductLookupQueryHandlerTests
{
    [Fact]
    public async Task Handle_ResolvesProductPriceAndOutletStockAndCachesResult()
    {
        var variant = new PosProductLookupVariantReadModel(
            10, 20, "Product", "Default", "barcode", "sku", "image", 12m, 10m, true);
        var store = new FakeStore { BarcodeVariant = variant, StockQuantity = 7 };
        var cache = new FakeCache();
        var priceCalculator = new FakePriceCalculator
        {
            Result = new PosCartPriceReadModel(
                10, "Product", "Default", 12m, 15m, 0m, 15m,
                10m, 1.5m, 16.5m, false, 4, "Campaign", [])
        };
        var handler = CreateHandler(store, cache, priceCalculator);
        using var cancellation = new CancellationTokenSource();

        var result = await handler.Handle(
            new GetPosProductLookupQuery("barcode", PosProductLookupMode.Barcode, 99),
            cancellation.Token);

        Assert.Equal(10, result?.VariantId);
        Assert.Equal(15m, result?.EffectivePrice);
        Assert.Equal("Campaign", result?.AppliedRuleName);
        Assert.Equal(7, result?.StockQty);
        Assert.True(result?.InStock);
        Assert.False(result?.StockFromCache);
        Assert.Equal(PosProductLookupMode.Barcode, cache.LookupMode);
        Assert.Equal("1:1:10:0:v1:barcode", cache.LookupQuery);
        Assert.Equal(99, cache.LookupOutletId);
        Assert.Equal(99, store.StockOutletId);
        Assert.Equal(new PosCartPriceLine(10, 99, 1), Assert.Single(priceCalculator.Items));
        Assert.True(priceCalculator.ReadFromCache);
        Assert.Equal(cancellation.Token, cache.CancellationToken);
        Assert.Equal(cancellation.Token, store.CancellationToken);
        Assert.Equal(cancellation.Token, priceCalculator.CancellationToken);
        Assert.Same(result, cache.SavedLookup);
    }

    [Fact]
    public async Task Handle_UsesProductBarcodeFallbackAndCachedStock()
    {
        var variant = new PosProductLookupVariantReadModel(
            10, 20, "Product", "Default", "product-barcode", "sku", null, 12m, 0m, true);
        var store = new FakeStore { FallbackVariant = variant };
        var cache = new FakeCache { CachedStock = 4 };
        var priceCalculator = new FakePriceCalculator();
        var handler = CreateHandler(store, cache, priceCalculator);

        var result = await handler.Handle(
            new GetPosProductLookupQuery("product-barcode", PosProductLookupMode.Barcode, 99),
            CancellationToken.None);

        Assert.Equal(10, result?.VariantId);
        Assert.Equal(4, result?.StockQty);
        Assert.True(result?.StockFromCache);
        Assert.Equal(1, store.FallbackCalls);
        Assert.Equal(0, store.StockCalls);
    }

    [Fact]
    public async Task Handle_ReturnsCachedLookupWithoutReadingOrPricing()
    {
        var cached = new PosProductLookupReadModel(
            10, 20, "Product", "Default", "barcode", "sku", null,
            12m, 15m, 10m, null, 5, true, true);
        var store = new FakeStore();
        var cache = new FakeCache { CachedLookup = cached };
        var priceCalculator = new FakePriceCalculator();
        var handler = CreateHandler(store, cache, priceCalculator);

        var result = await handler.Handle(
            new GetPosProductLookupQuery("barcode", PosProductLookupMode.Barcode, 99),
            CancellationToken.None);

        Assert.Same(cached, result);
        Assert.Equal(0, store.LookupCalls);
        Assert.Equal(0, priceCalculator.Calls);
    }

    [Fact]
    public async Task Handle_PropagatesCancellationToLookupCacheAndPorts()
    {
        using var cancellation = new CancellationTokenSource();
        var variant = new PosProductLookupVariantReadModel(
            10, 20, "Product", "Default", "barcode", "sku", null, 12m, 0m, true);
        var store = new FakeStore { BarcodeVariant = variant };
        var cache = new FakeCache();
        var priceCalculator = new FakePriceCalculator();
        var handler = CreateHandler(store, cache, priceCalculator);

        await handler.Handle(
            new GetPosProductLookupQuery("barcode", PosProductLookupMode.Barcode, 99),
            cancellation.Token);

        Assert.Equal(cancellation.Token, cache.CancellationToken);
        Assert.Equal(cancellation.Token, store.CancellationToken);
        Assert.Equal(cancellation.Token, priceCalculator.CancellationToken);
    }

    [Fact]
    public async Task Handle_ScopesLookupCacheKeyToTheBusinessSoBusinessesNeverShareCachedProducts()
    {
        var variant = new PosProductLookupVariantReadModel(
            10, 20, "Product", "Default", "barcode", "sku", null, 12m, 0m, true);
        var cacheForBusiness1 = new FakeCache();
        var cacheForBusiness2 = new FakeCache();

        await CreateHandler(new FakeStore { BarcodeVariant = variant }, cacheForBusiness1, new FakePriceCalculator(), businessId: 1)
            .Handle(new GetPosProductLookupQuery("barcode", PosProductLookupMode.Barcode, 99), CancellationToken.None);
        await CreateHandler(new FakeStore { BarcodeVariant = variant }, cacheForBusiness2, new FakePriceCalculator(), businessId: 2)
            .Handle(new GetPosProductLookupQuery("barcode", PosProductLookupMode.Barcode, 99), CancellationToken.None);

        Assert.NotEqual(cacheForBusiness1.LookupQuery, cacheForBusiness2.LookupQuery);
        Assert.StartsWith("1:", cacheForBusiness1.LookupQuery);
        Assert.StartsWith("2:", cacheForBusiness2.LookupQuery);
    }

    [Fact]
    public async Task Handle_KeysTheLookupCacheByPricingVersionAndBypassesItWhenTheBackendIsUnavailable()
    {
        var variant = new PosProductLookupVariantReadModel(
            10, 20, "Product", "Default", "barcode", "sku", null, 12m, 0m, true);
        var versioned = new FakeCache();
        var version = new FixedPricingCacheVersion("abc");
        await CreateHandler(new FakeStore { BarcodeVariant = variant }, versioned, new FakePriceCalculator(), cacheVersion: version)
            .Handle(new GetPosProductLookupQuery("barcode", PosProductLookupMode.Barcode, 99), CancellationToken.None);
        version.Token = "def";
        var bumped = new FakeCache();
        await CreateHandler(new FakeStore { BarcodeVariant = variant }, bumped, new FakePriceCalculator(), cacheVersion: version)
            .Handle(new GetPosProductLookupQuery("barcode", PosProductLookupMode.Barcode, 99), CancellationToken.None);

        Assert.Equal("1:1:10:0:abc:barcode", versioned.LookupQuery);
        Assert.Equal("1:1:10:0:def:barcode", bumped.LookupQuery);

        var cached = new PosProductLookupReadModel(
            10, 20, "Product", "Default", "barcode", "sku", null, 12m, 15m, 10m, null, 5, true, true);
        var outage = new FakeCache { CachedLookup = cached };
        var store = new FakeStore { BarcodeVariant = variant };
        var result = await CreateHandler(store, outage, new FakePriceCalculator(), cacheVersion: new FixedPricingCacheVersion(null))
            .Handle(new GetPosProductLookupQuery("barcode", PosProductLookupMode.Barcode, 99), CancellationToken.None);

        Assert.NotSame(cached, result);
        Assert.Equal(1, store.LookupCalls);
        Assert.Null(outage.SavedLookup);
    }

    private static GetPosProductLookupQueryHandler CreateHandler(
        IPosProductLookupStore store,
        IPosProductLookupCache cache,
        IPosCartPriceCalculator priceCalculator,
        long businessId = 1,
        FixedPricingCacheVersion? cacheVersion = null) =>
        new(store, cache, priceCalculator, new FixedTaxPolicyProvider(new SaleTaxPolicy(true, 10m, false)), new FixedBusinessScope(businessId), cacheVersion ?? new FixedPricingCacheVersion());

    private sealed class FakeStore : IPosProductLookupStore
    {
        public PosProductLookupVariantReadModel? BarcodeVariant { get; init; }
        public PosProductLookupVariantReadModel? FallbackVariant { get; init; }
        public int StockQuantity { get; init; }
        public int LookupCalls { get; private set; }
        public int FallbackCalls { get; private set; }
        public int StockCalls { get; private set; }
        public long? StockOutletId { get; private set; }
        public CancellationToken CancellationToken { get; private set; }

        public Task<PosProductLookupVariantReadModel?> FindVariantByBarcodeAsync(
            string barcode,
            CancellationToken cancellationToken)
        {
            LookupCalls++;
            CancellationToken = cancellationToken;
            return Task.FromResult(BarcodeVariant);
        }

        public Task<PosProductLookupVariantReadModel?> FindFirstActiveProductVariantByBarcodeAsync(
            string barcode,
            CancellationToken cancellationToken)
        {
            FallbackCalls++;
            CancellationToken = cancellationToken;
            return Task.FromResult(FallbackVariant);
        }

        public Task<PosProductLookupVariantReadModel?> FindVariantBySkuAsync(
            string sku,
            CancellationToken cancellationToken)
        {
            LookupCalls++;
            CancellationToken = cancellationToken;
            return Task.FromResult<PosProductLookupVariantReadModel?>(null);
        }

        public Task<PosProductLookupVariantReadModel?> FindVariantByIdAsync(
            long variantId,
            CancellationToken cancellationToken)
        {
            LookupCalls++;
            CancellationToken = cancellationToken;
            return Task.FromResult<PosProductLookupVariantReadModel?>(null);
        }

        public Task<int> GetOutletStockAsync(
            long variantId,
            long outletId,
            CancellationToken cancellationToken)
        {
            StockCalls++;
            StockOutletId = outletId;
            CancellationToken = cancellationToken;
            return Task.FromResult(StockQuantity);
        }
    }

    private sealed class FakeCache : IPosProductLookupCache
    {
        public PosProductLookupReadModel? CachedLookup { get; init; }
        public PosProductLookupReadModel? SavedLookup { get; private set; }
        public int? CachedStock { get; init; }
        public PosProductLookupMode LookupMode { get; private set; }
        public string? LookupQuery { get; private set; }
        public long? LookupOutletId { get; private set; }
        public CancellationToken CancellationToken { get; private set; }

        public Task<PosProductLookupReadModel?> GetLookupAsync(
            PosProductLookupMode mode,
            string query,
            long? outletId,
            CancellationToken cancellationToken)
        {
            LookupMode = mode;
            LookupQuery = query;
            LookupOutletId = outletId;
            CancellationToken = cancellationToken;
            return Task.FromResult(CachedLookup);
        }

        public Task SetLookupAsync(
            PosProductLookupMode mode,
            string query,
            long? outletId,
            PosProductLookupReadModel result,
            CancellationToken cancellationToken)
        {
            SavedLookup = result;
            CancellationToken = cancellationToken;
            return Task.CompletedTask;
        }

        public Task<int?> GetStockAsync(
            long variantId,
            long outletId,
            CancellationToken cancellationToken)
        {
            CancellationToken = cancellationToken;
            return Task.FromResult(CachedStock);
        }

        public Task SetStockAsync(
            long variantId,
            long outletId,
            int quantity,
            CancellationToken cancellationToken)
        {
            CancellationToken = cancellationToken;
            return Task.CompletedTask;
        }
    }

    private sealed class FakePriceCalculator : IPosCartPriceCalculator
    {
        public PosCartPriceReadModel Result { get; init; } =
            new(0, string.Empty, string.Empty, 12m, 12m, 0m, 12m, 0m, 0m, 12m, false, null, null, []);
        public int Calls { get; private set; }
        public IReadOnlyList<PosCartPriceLine> Items { get; private set; } = [];
        public bool ReadFromCache { get; private set; }
        public CancellationToken CancellationToken { get; private set; }

        public Task<IReadOnlyList<PosCartPriceReadModel>> CalculateAsync(
            IReadOnlyList<PosCartPriceLine> items,
            bool readFromCache,
            CancellationToken cancellationToken)
        {
            Calls++;
            Items = items;
            ReadFromCache = readFromCache;
            CancellationToken = cancellationToken;
            return Task.FromResult<IReadOnlyList<PosCartPriceReadModel>>([Result]);
        }
    }
}
