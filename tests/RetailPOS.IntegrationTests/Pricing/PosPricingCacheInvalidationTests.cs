using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using RetailPOS.Application.Pos.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Pos;

namespace RetailPOS.Tests.Pricing;

/// <summary>Phase 2.1 Workstream F: price/lookup cache invalidation is Business/Outlet scoped, post-commit and outage tolerant.</summary>
public sealed class PosPricingCacheInvalidationTests
{
    private static IDistributedCache NewCache() =>
        new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

    private static PosPricingCacheVersion NewVersion(IDistributedCache? cache = null) =>
        new(cache ?? NewCache(), NullLogger<PosPricingCacheVersion>.Instance);

    [Fact]
    public async Task Tokens_AreStableUntilInvalidatedAndAreScopedToBusinessAndOutlet()
    {
        var cache = NewCache();
        var reader = NewVersion(cache);

        var business1 = await reader.GetTokenAsync(1, null, CancellationToken.None);
        var business1Outlet10 = await reader.GetTokenAsync(1, 10, CancellationToken.None);
        var business1Outlet11 = await reader.GetTokenAsync(1, 11, CancellationToken.None);
        var business2 = await reader.GetTokenAsync(2, null, CancellationToken.None);
        Assert.Equal(business1, await NewVersion(cache).GetTokenAsync(1, null, CancellationToken.None));

        // Another request scope invalidates one outlet of Business 1.
        await NewVersion(cache).InvalidateAsync(1, 10, CancellationToken.None);
        var fresh = NewVersion(cache);
        Assert.Equal(business1, await fresh.GetTokenAsync(1, null, CancellationToken.None));
        Assert.NotEqual(business1Outlet10, await fresh.GetTokenAsync(1, 10, CancellationToken.None));
        Assert.Equal(business1Outlet11, await fresh.GetTokenAsync(1, 11, CancellationToken.None));
        Assert.Equal(business2, await fresh.GetTokenAsync(2, null, CancellationToken.None));

        // A Business-wide invalidation orphans every outlet of that Business and nothing of another Business.
        await NewVersion(cache).InvalidateAsync(1, null, CancellationToken.None);
        var afterBusiness = NewVersion(cache);
        Assert.NotEqual(business1, await afterBusiness.GetTokenAsync(1, null, CancellationToken.None));
        Assert.NotEqual(business1Outlet11, await afterBusiness.GetTokenAsync(1, 11, CancellationToken.None));
        Assert.Equal(business2, await afterBusiness.GetTokenAsync(2, null, CancellationToken.None));
    }

    [Fact]
    public async Task InvalidationWithinOneScope_IsVisibleToTheSameScopeImmediately()
    {
        var version = NewVersion();
        var before = await version.GetTokenAsync(1, 10, CancellationToken.None);

        await version.InvalidateAsync(1, 10, CancellationToken.None);

        Assert.NotEqual(before, await version.GetTokenAsync(1, 10, CancellationToken.None));
    }

    [Fact]
    public async Task CacheBackendOutage_ReturnsNoTokenAndInvalidationNeverThrows()
    {
        var cache = new Mock<IDistributedCache>();
        cache.Setup(candidate => candidate.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Redis is down"));
        cache.Setup(candidate => candidate.SetAsync(
                It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Redis is down"));
        var version = NewVersion(cache.Object);

        Assert.Null(await version.GetTokenAsync(1, 10, CancellationToken.None));
        await version.InvalidateAsync(1, 10, CancellationToken.None);

        cache.Verify(
            candidate => candidate.SetAsync(
                "pos:ver:1:10", It.IsAny<byte[]>(), It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3));
    }

    [Fact]
    public async Task Invalidation_IsRetriedAfterATransientFailure()
    {
        var calls = 0;
        var cache = new Mock<IDistributedCache>();
        cache.Setup(candidate => candidate.SetAsync(
                It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()))
            .Returns(() => ++calls < 3 ? Task.FromException(new InvalidOperationException("blip")) : Task.CompletedTask);

        await NewVersion(cache.Object).InvalidateAsync(1, null, CancellationToken.None);

        Assert.Equal(3, calls);
    }

    private sealed class RecordingVersion : IPosPricingCacheVersion
    {
        public List<(long BusinessId, long? OutletId)> Invalidations { get; } = [];

        public Task<string?> GetTokenAsync(long businessId, long? outletId, CancellationToken cancellationToken) =>
            Task.FromResult<string?>("t");

        public Task InvalidateAsync(long businessId, long? outletId, CancellationToken cancellationToken)
        {
            Invalidations.Add((businessId, outletId));
            return Task.CompletedTask;
        }
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:");
        public RecordingVersion Version { get; } = new();
        public RetailPOSDbContext Db { get; private set; } = null!;

        public static async Task<Harness> CreateAsync()
        {
            var harness = new Harness();
            await harness._connection.OpenAsync();
            await using (var setup = new RetailPOSDbContext(
                             new DbContextOptionsBuilder<RetailPOSDbContext>().UseSqlite(harness._connection).Options))
            {
                await setup.Database.EnsureCreatedAsync();
                await TestBusinesses.SeedAsync(setup, 1, 2);
                setup.Categories.AddRange(
                    new Category { Id = 1, BusinessId = 1, Name = "Cat 1" },
                    new Category { Id = 2, BusinessId = 2, Name = "Cat 2" });
                setup.Outlets.AddRange(
                    new Outlet { Id = 11, BusinessId = 1, Name = "Outlet 1" },
                    new Outlet { Id = 12, BusinessId = 1, Name = "Outlet 1b" },
                    new Outlet { Id = 22, BusinessId = 2, Name = "Outlet 2" });
                setup.Products.AddRange(
                    new Product { Id = 1, BusinessId = 1, CategoryId = 1, Name = "P1", BasePrice = 10m },
                    new Product { Id = 2, BusinessId = 2, CategoryId = 2, Name = "P2", BasePrice = 10m });
                setup.ProductVariants.AddRange(
                    new ProductVariant { Id = 1, BusinessId = 1, ProductId = 1, Name = "V1", Sku = "S1" },
                    new ProductVariant { Id = 2, BusinessId = 2, ProductId = 2, Name = "V2", Sku = "S2" });
                await setup.SaveChangesAsync();
            }

            var tracker = new PosPricingInvalidationTracker(harness.Version);
            harness.Db = new RetailPOSDbContext(new DbContextOptionsBuilder<RetailPOSDbContext>()
                .UseSqlite(harness._connection)
                .AddInterceptors(new PosPricingSaveChangesInterceptor(tracker), new PosPricingTransactionInterceptor(tracker))
                .Options);
            return harness;
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    [Fact]
    public async Task ProductAndVariantChanges_InvalidateOnlyTheirBusinessAfterTheSave()
    {
        await using var harness = await Harness.CreateAsync();
        var product = await harness.Db.Products.SingleAsync(candidate => candidate.Id == 1);
        product.BasePrice = 12m;
        Assert.Empty(harness.Version.Invalidations);

        await harness.Db.SaveChangesAsync();
        Assert.Equal([(1L, (long?)null)], harness.Version.Invalidations);

        harness.Version.Invalidations.Clear();
        var variant = await harness.Db.ProductVariants.SingleAsync(candidate => candidate.Id == 2);
        variant.PriceAdjustment = 3m;
        await harness.Db.SaveChangesAsync();
        Assert.Equal([(2L, (long?)null)], harness.Version.Invalidations);

        harness.Version.Invalidations.Clear();
        (await harness.Db.Categories.SingleAsync(candidate => candidate.Id == 1)).Name = "Renamed";
        await harness.Db.SaveChangesAsync();
        Assert.Empty(harness.Version.Invalidations);
    }

    [Fact]
    public async Task PriceRulesAndOverrides_InvalidateTheNarrowestAffectedScope()
    {
        await using var harness = await Harness.CreateAsync();

        harness.Db.PriceRules.Add(new PriceRule
        {
            BusinessId = 1, Name = "Global", RuleType = "global", DiscountType = "percentage", DiscountValue = 10m, IsActive = true
        });
        await harness.Db.SaveChangesAsync();
        Assert.Equal([(1L, (long?)null)], harness.Version.Invalidations);

        harness.Version.Invalidations.Clear();
        var outletRule = new PriceRule
        {
            BusinessId = 1, Name = "Outlet", RuleType = "outlet", TargetId = 11, DiscountType = "percentage", DiscountValue = 5m, IsActive = true
        };
        harness.Db.PriceRules.Add(outletRule);
        await harness.Db.SaveChangesAsync();
        Assert.Equal([(1L, (long?)11L)], harness.Version.Invalidations);

        // Re-targeting a rule invalidates what it used to affect as well as what it now affects.
        harness.Version.Invalidations.Clear();
        outletRule.RuleType = "global";
        outletRule.TargetId = null;
        await harness.Db.SaveChangesAsync();
        Assert.Equivalent(new[] { (1L, (long?)11L), (1L, (long?)null) }, harness.Version.Invalidations);

        harness.Version.Invalidations.Clear();
        harness.Db.OutletPriceOverrides.Add(new OutletPriceOverride
        {
            BusinessId = 1, OutletId = 12, ProductVariantId = 1, OverrideType = "fixed", OverrideValue = 9m, IsActive = true
        });
        await harness.Db.SaveChangesAsync();
        Assert.Equal([(1L, (long?)12L)], harness.Version.Invalidations);

        harness.Version.Invalidations.Clear();
        harness.Db.OutletPriceOverrides.Remove(await harness.Db.OutletPriceOverrides.SingleAsync());
        await harness.Db.SaveChangesAsync();
        Assert.Equal([(1L, (long?)12L)], harness.Version.Invalidations);
    }

    [Fact]
    public async Task InsideATransaction_InvalidationWaitsForCommitAndRollbackDropsIt()
    {
        await using var harness = await Harness.CreateAsync();

        await using (var rolledBack = await harness.Db.Database.BeginTransactionAsync())
        {
            (await harness.Db.Products.SingleAsync(candidate => candidate.Id == 1)).BasePrice = 99m;
            await harness.Db.SaveChangesAsync();
            Assert.Empty(harness.Version.Invalidations);
            await rolledBack.RollbackAsync();
        }

        Assert.Empty(harness.Version.Invalidations);
        harness.Db.ChangeTracker.Clear();

        await using (var committed = await harness.Db.Database.BeginTransactionAsync())
        {
            (await harness.Db.Products.SingleAsync(candidate => candidate.Id == 1)).BasePrice = 15m;
            await harness.Db.SaveChangesAsync();
            Assert.Empty(harness.Version.Invalidations);
            await committed.CommitAsync();
        }

        Assert.Equal([(1L, (long?)null)], harness.Version.Invalidations);
    }

    [Fact]
    public async Task AFailedSave_NeverInvalidatesAnything()
    {
        await using var harness = await Harness.CreateAsync();
        harness.Db.PriceRules.Add(new PriceRule
        {
            BusinessId = 999, Name = "Orphan", RuleType = "global", DiscountType = "percentage", DiscountValue = 1m, IsActive = true
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => harness.Db.SaveChangesAsync());

        Assert.Empty(harness.Version.Invalidations);
    }
}
