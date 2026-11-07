using RetailPOS.Application.Pos.Abstractions;
using RetailPOS.Application.Pos.Queries;
using RetailPOS.Application.Pos.ReadModels;
using RetailPOS.Application.Sales.Checkout;

namespace RetailPOS.Tests.Pos;

public sealed class CalculatePosCartPricesQueryHandlerTests
{
    private static readonly SaleTaxPolicy Vat5 = new(true, 5m, false);

    private static PosCartPriceCalculator Calculator(
        FakePricingStore pricingStore,
        FakePriceCache cache,
        SaleTaxPolicy? policy = null,
        long? businessId = 1,
        FixedPricingCacheVersion? cacheVersion = null) =>
        new(pricingStore, cache, new FixedTaxPolicyProvider(policy ?? Vat5), new FixedBusinessScope(businessId), cacheVersion ?? new FixedPricingCacheVersion());

    private static FakePricingStore StoreFor(decimal? taxRate, string? classification = null, decimal basePrice = 12m) =>
        new()
        {
            Data = new PosCartPricingDataReadModel(
                [new PosCartPricingVariantReadModel(
                    10, 20, 30, "Product", "Variant", basePrice, 0m, 5m, 0m, taxRate, classification, 1)],
                [],
                [])
        };

    [Fact]
    public async Task Handle_AppliesOutletOverrideAndBestQuantityRuleThenCachesResults()
    {
        var variant = new PosCartPricingVariantReadModel(
            10, 20, 30, "Product", "Variant", 20m, 2m, 8m, 1m, 10m);
        var pricingStore = new FakePricingStore
        {
            Data = new PosCartPricingDataReadModel(
                [variant],
                [new PosCartPricingOverrideReadModel(99, 10, "fixed", 30m)],
                [
                    new PosCartPricingRuleReadModel(1, 1, "Low priority", "global", null, "fixed", 4m, 1),
                    new PosCartPricingRuleReadModel(2, 10, "Campaign", "variant", 10, "percentage", 20m, 2)
                ])
        };
        var cache = new FakePriceCache();
        var handler = new CalculatePosCartPricesQueryHandler(Calculator(pricingStore, cache));
        using var cancellation = new CancellationTokenSource();

        var results = await handler.Handle(
            new CalculatePosCartPricesQuery([new PosCartPriceLine(10, 99, 2)]),
            cancellation.Token);

        var result = Assert.Single(results);
        Assert.Equal(22m, result.BasePrice);
        Assert.Equal(30m, result.OutletPrice);
        Assert.Equal(6m, result.DiscountAmount);
        Assert.Equal(24m, result.FinalPrice);
        // The product's own 10% overrides the Business default of 5%.
        Assert.Equal(10m, result.TaxRate);
        Assert.Equal(2.4m, result.TaxAmount);
        Assert.Equal(26.4m, result.FinalPriceWithTax);
        Assert.True(result.OutletOverrideApplied);
        Assert.Equal("Campaign", result.AppliedRuleName);
        Assert.Equal(99, Assert.Single(pricingStore.OutletIds));
        Assert.Same(result, Assert.Single(cache.Results));
        Assert.Equal(cancellation.Token, pricingStore.CancellationToken);
        Assert.Equal(cancellation.Token, cache.CancellationToken);
    }

    [Fact]
    public async Task Handle_UsesBasePriceWithoutOutletAndPreservesFixedDiscountCap()
    {
        var pricingStore = new FakePricingStore
        {
            Data = new PosCartPricingDataReadModel(
                [new PosCartPricingVariantReadModel(
                    10, 20, 30, "Product", "Variant", 12m, 0m, 5m, 0m, 10m)],
                [],
                [new PosCartPricingRuleReadModel(1, 1, "Large discount", "global", null, "fixed", 100m, 1)])
        };
        var cache = new FakePriceCache();
        var handler = new CalculatePosCartPricesQueryHandler(Calculator(pricingStore, cache));

        var result = Assert.Single(await handler.Handle(
            new CalculatePosCartPricesQuery([new PosCartPriceLine(10, null, 1)]),
            CancellationToken.None));

        Assert.Equal(12m, result.OutletPrice);
        Assert.Equal(12m, result.DiscountAmount);
        Assert.Equal(0m, result.FinalPrice);
        Assert.Equal(0m, result.TaxAmount);
        Assert.False(result.OutletOverrideApplied);
        Assert.Equal(0, Assert.Single(cache.Outlets));
    }

    [Fact]
    public async Task Handle_ReadsCachedResultBeforeLoadingPricingData()
    {
        var cached = new PosCartPriceReadModel(
            10, "Cached product", "Cached variant", 20m, 30m, 0m, 30m,
            10m, 3m, 33m, true, null, null, []);
        var pricingStore = new FakePricingStore();
        var cache = new FakePriceCache();
        var scope = $"1:1:{Vat5.RatePercent}:0:v1";
        cache.CachedResults[(scope, 10, 99, 2)] = cached;
        var handler = new CalculatePosCartPricesQueryHandler(Calculator(pricingStore, cache));

        var result = Assert.Single(await handler.Handle(
            new CalculatePosCartPricesQuery(
                [new PosCartPriceLine(10, 99, 2)],
                ReadFromCache: true),
            CancellationToken.None));

        Assert.Same(cached, result);
        Assert.False(pricingStore.LoadWasCalled);
        Assert.Empty(cache.Results);
    }

    [Fact]
    public async Task Handle_DoesNotServeCachedPriceCalculatedForAnotherBusinessOrTaxPolicy()
    {
        var cached = new PosCartPriceReadModel(
            10, "Other business", "Variant", 20m, 30m, 0m, 30m, 10m, 3m, 33m, true, null, null, []);
        var cache = new FakePriceCache();
        cache.CachedResults[("2:1:5:0:v1", 10, 0, 1)] = cached;
        cache.CachedResults[("1:1:7:0:v1", 10, 0, 1)] = cached;
        cache.CachedResults[("1:1:5:0:v0", 10, 0, 1)] = cached;
        var pricingStore = StoreFor(null);

        var result = Assert.Single(await Calculator(pricingStore, cache).CalculateAsync(
            [new PosCartPriceLine(10, null, 1)], true, CancellationToken.None));

        Assert.NotSame(cached, result);
        Assert.True(pricingStore.LoadWasCalled);
    }

    [Fact]
    public async Task Handle_BypassesTheCacheCompletelyWhenTheVersionBackendIsUnavailable()
    {
        var cached = new PosCartPriceReadModel(
            10, "Stale", "Variant", 20m, 30m, 0m, 30m, 10m, 3m, 33m, true, null, null, []);
        var cache = new FakePriceCache();
        cache.CachedResults[("1:1:5:0:", 10, 0, 1)] = cached;
        var pricingStore = StoreFor(null);

        var result = Assert.Single(await Calculator(pricingStore, cache, cacheVersion: new FixedPricingCacheVersion(null))
            .CalculateAsync([new PosCartPriceLine(10, null, 1)], true, CancellationToken.None));

        Assert.NotSame(cached, result);
        Assert.True(pricingStore.LoadWasCalled);
        Assert.Empty(cache.Results);
    }

    [Fact]
    public async Task Handle_CheckoutRepricingIgnoresAStaleCachedPriceAndRefreshesTheCache()
    {
        var stale = new PosCartPriceReadModel(
            10, "Stale", "Variant", 20m, 20m, 0m, 20m, 0m, 0m, 20m, false, null, null, []);
        var cache = new FakePriceCache();
        cache.CachedResults[("1:1:5:0:v1", 10, 0, 1)] = stale;
        var pricingStore = StoreFor(null, basePrice: 15m);

        var result = Assert.Single(await Calculator(pricingStore, cache).CalculateAsync(
            [new PosCartPriceLine(10, null, 1)], false, CancellationToken.None));

        Assert.NotSame(stale, result);
        Assert.Equal(15m, result.BasePrice);
        Assert.True(pricingStore.LoadWasCalled);
        Assert.Same(result, Assert.Single(cache.Results));
    }

    [Fact]
    public async Task Handle_FallsBackToBusinessDefaultWhenProductHasNoTaxConfiguration()
    {
        var result = Assert.Single(await Calculator(StoreFor(null), new FakePriceCache()).CalculateAsync(
            [new PosCartPriceLine(10, null, 1)], false, CancellationToken.None));

        Assert.Equal(5m, result.TaxRate);
        Assert.Equal(0.6m, result.TaxAmount);
        Assert.Equal(12.6m, result.FinalPriceWithTax);
        Assert.Equal(TaxClassifications.Standard, result.TaxClassification);
    }

    [Fact]
    public async Task Handle_TreatsExplicitZeroProductRateAsZeroRatedNotMissing()
    {
        var result = Assert.Single(await Calculator(StoreFor(0m), new FakePriceCache()).CalculateAsync(
            [new PosCartPriceLine(10, null, 1)], false, CancellationToken.None));

        Assert.Equal(0m, result.TaxRate);
        Assert.Equal(0m, result.TaxAmount);
        Assert.Equal(12m, result.FinalPriceWithTax);
        Assert.Equal(TaxClassifications.ZeroRated, result.TaxClassification);
    }

    [Fact]
    public async Task Handle_ExemptClassificationIgnoresAnyRate()
    {
        var result = Assert.Single(await Calculator(StoreFor(15m, TaxClassifications.Exempt), new FakePriceCache()).CalculateAsync(
            [new PosCartPriceLine(10, null, 1)], false, CancellationToken.None));

        Assert.Equal(0m, result.TaxRate);
        Assert.Equal(0m, result.TaxAmount);
        Assert.Equal(TaxClassifications.Exempt, result.TaxClassification);
    }

    [Fact]
    public async Task Handle_InclusivePricesExtractTaxAndKeepThePayablePrice()
    {
        var policy = new SaleTaxPolicy(true, 5m, true);

        var result = Assert.Single(await Calculator(StoreFor(10m, basePrice: 24m), new FakePriceCache(), policy).CalculateAsync(
            [new PosCartPriceLine(10, null, 1)], false, CancellationToken.None));

        Assert.True(result.PricesIncludeTax);
        Assert.Equal(24m, result.FinalPrice);
        Assert.Equal(2.18m, result.TaxAmount);
        Assert.Equal(24m, result.FinalPriceWithTax);
    }

    [Fact]
    public async Task Handle_ChargesNoTaxWhenTheBusinessSwitchIsOff()
    {
        var policy = new SaleTaxPolicy(false, 5m, false);

        var result = Assert.Single(await Calculator(StoreFor(10m), new FakePriceCache(), policy).CalculateAsync(
            [new PosCartPriceLine(10, null, 1)], false, CancellationToken.None));

        Assert.Equal(0m, result.TaxAmount);
        Assert.Equal(TaxClassifications.None, result.TaxClassification);
    }

    [Fact]
    public async Task Handle_RequiresABusinessContext()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Calculator(StoreFor(null), new FakePriceCache(), businessId: null)
            .CalculateAsync([new PosCartPriceLine(10, null, 1)], false, CancellationToken.None));
    }

    private sealed class FakePricingStore : IPosCartPricingStore
    {
        public PosCartPricingDataReadModel Data { get; init; } = new([], [], []);
        public IReadOnlyCollection<long> OutletIds { get; private set; } = [];
        public CancellationToken CancellationToken { get; private set; }
        public bool LoadWasCalled { get; private set; }

        public Task<PosCartPricingDataReadModel> LoadPricingDataAsync(
            IReadOnlyCollection<long> variantIds,
            IReadOnlyCollection<long> outletIds,
            DateTime asOfUtc,
            CancellationToken cancellationToken)
        {
            LoadWasCalled = true;
            OutletIds = outletIds;
            CancellationToken = cancellationToken;
            return Task.FromResult(Data);
        }
    }

    private sealed class FakePriceCache : IPosCartPriceCache
    {
        public List<PosCartPriceReadModel> Results { get; } = [];
        public List<long> Outlets { get; } = [];
        public Dictionary<(string Scope, long VariantId, long OutletId, decimal Quantity), PosCartPriceReadModel> CachedResults { get; } = [];
        public CancellationToken CancellationToken { get; private set; }

        public Task<PosCartPriceReadModel?> GetAsync(
            string scope,
            long variantId,
            long? outletId,
            decimal quantity,
            CancellationToken cancellationToken)
        {
            CachedResults.TryGetValue((scope, variantId, outletId ?? 0, quantity), out var result);
            return Task.FromResult(result);
        }

        public Task SetAsync(
            string scope,
            long variantId,
            long? outletId,
            decimal quantity,
            PosCartPriceReadModel result,
            CancellationToken cancellationToken)
        {
            Results.Add(result);
            Outlets.Add(outletId ?? 0);
            CancellationToken = cancellationToken;
            return Task.CompletedTask;
        }
    }
}
