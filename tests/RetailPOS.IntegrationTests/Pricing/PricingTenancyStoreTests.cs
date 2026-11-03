using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Pricing.Commands;
using RetailPOS.Application.Pricing.ReadModels;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Pos;
using RetailPOS.Infrastructure.Pricing;

namespace RetailPOS.Tests.Pricing;

/// <summary>Phase 2.1 Workstream C: pricing rules and outlet overrides are isolated per Business at the store level.</summary>
public sealed class PricingTenancyStoreTests
{
    private static async Task<(SqliteConnection Connection, RetailPOSDbContext Db)> CreateAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var db = new RetailPOSDbContext(new DbContextOptionsBuilder<RetailPOSDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        await TestBusinesses.SeedAsync(db, 1, 2);
        db.Categories.AddRange(
            new Category { Id = 1, BusinessId = 1, Name = "Cat 1" },
            new Category { Id = 2, BusinessId = 2, Name = "Cat 2" });
        db.Outlets.AddRange(
            new Outlet { Id = 11, BusinessId = 1, Name = "Outlet 1" },
            new Outlet { Id = 22, BusinessId = 2, Name = "Outlet 2" });
        db.Products.AddRange(
            new Product { Id = 1, BusinessId = 1, CategoryId = 1, Name = "P1", BasePrice = 100m },
            new Product { Id = 2, BusinessId = 2, CategoryId = 2, Name = "P2", BasePrice = 100m });
        db.ProductVariants.AddRange(
            new ProductVariant { Id = 1, BusinessId = 1, ProductId = 1, Name = "V1", Sku = "S1" },
            new ProductVariant { Id = 2, BusinessId = 2, ProductId = 2, Name = "V2", Sku = "S2" });
        await db.SaveChangesAsync();
        return (connection, db);
    }

    private static PriceRuleWriteData Rule(string name, string type = "global", long? target = null, decimal value = 10m) =>
        new(name, null, type, target, "percentage", value, 1, 0, null, null, true);

    [Fact]
    public async Task PriceRules_AreInvisibleAndImmutableAcrossBusinesses()
    {
        var (connection, db) = await CreateAsync();
        await using var _ = connection;
        await using var __ = db;
        var business1 = new PriceRuleCommandStore(db, new TestBusinessScope(1));
        var business2 = new PriceRuleCommandStore(db, new TestBusinessScope(2));
        var created = await new CreatePriceRuleCommandHandler(business1)
            .Handle(new CreatePriceRuleCommand(Rule("Shared name")), CancellationToken.None);

        Assert.Equal(1, (await db.PriceRules.AsNoTracking().SingleAsync()).BusinessId);
        Assert.Null(await business2.GetByIdAsync(created.Id, CancellationToken.None));
        Assert.False(await business2.NameExistsAsync("Shared name", null, CancellationToken.None));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => new UpdatePriceRuleCommandHandler(business2)
            .Handle(new UpdatePriceRuleCommand(created.Id, Rule("Hijacked")), CancellationToken.None));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => new DeletePriceRuleCommandHandler(business2)
            .Handle(new DeletePriceRuleCommand(created.Id), CancellationToken.None));
        Assert.Equal("Shared name", (await db.PriceRules.AsNoTracking().SingleAsync()).Name);

        // The same name is allowed in another Business.
        var other = await new CreatePriceRuleCommandHandler(business2)
            .Handle(new CreatePriceRuleCommand(Rule("Shared name")), CancellationToken.None);
        Assert.Equal(2, (await db.PriceRules.AsNoTracking().SingleAsync(rule => rule.Id == other.Id)).BusinessId);
    }

    [Fact]
    public async Task PriceRules_CannotTargetAnotherBusinessesCatalogue()
    {
        var (connection, db) = await CreateAsync();
        await using var _ = connection;
        await using var __ = db;
        var handler = new CreatePriceRuleCommandHandler(new PriceRuleCommandStore(db, new TestBusinessScope(1)));

        foreach (var (type, foreign) in new[] { ("variant", 2L), ("product", 2L), ("category", 2L), ("outlet", 22L) })
        {
            await Assert.ThrowsAsync<KeyNotFoundException>(() => handler.Handle(
                new CreatePriceRuleCommand(Rule($"Foreign {type}", type, foreign)), CancellationToken.None));
        }

        Assert.Empty(await db.PriceRules.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task OutletOverrides_AreIsolatedAndRejectForeignOutletsAndVariants()
    {
        var (connection, db) = await CreateAsync();
        await using var _ = connection;
        await using var __ = db;
        var store1 = new OutletPriceOverrideCommandStore(db, new TestBusinessScope(1));
        var store2 = new OutletPriceOverrideCommandStore(db, new TestBusinessScope(2));
        var authorizer = new AllowAll();
        var create1 = new CreateOutletPriceOverrideCommandHandler(store1, authorizer);

        var created = await create1.Handle(
            new CreateOutletPriceOverrideCommand(new OutletPriceOverrideWriteData(11, 1, "fixed", 50m, true)),
            CancellationToken.None);
        Assert.Equal(1, (await db.OutletPriceOverrides.AsNoTracking().SingleAsync()).BusinessId);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => create1.Handle(
            new CreateOutletPriceOverrideCommand(new OutletPriceOverrideWriteData(22, 1, "fixed", 50m, true)),
            CancellationToken.None));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => create1.Handle(
            new CreateOutletPriceOverrideCommand(new OutletPriceOverrideWriteData(11, 2, "fixed", 50m, true)),
            CancellationToken.None));

        Assert.Null(await store2.GetByIdAsync(created.Id, CancellationToken.None));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => new UpdateOutletPriceOverrideCommandHandler(store2, authorizer)
            .Handle(new UpdateOutletPriceOverrideCommand(created.Id, "fixed", 1m, true), CancellationToken.None));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => new DeleteOutletPriceOverrideCommandHandler(store2, authorizer)
            .Handle(new DeleteOutletPriceOverrideCommand(created.Id), CancellationToken.None));
        Assert.Equal(50m, (await db.OutletPriceOverrides.AsNoTracking().SingleAsync()).OverrideValue);

        var readStore2 = new OutletPriceOverrideReadStore(db, new TestBusinessScope(2));
        Assert.Empty(await readStore2.GetByOutletAsync(11, CancellationToken.None));
        var readStore1 = new OutletPriceOverrideReadStore(db, new TestBusinessScope(1));
        Assert.Single(await readStore1.GetByOutletAsync(11, CancellationToken.None));
    }

    [Fact]
    public async Task CartPricing_IgnoresRulesAndOverridesOfOtherBusinesses()
    {
        var (connection, db) = await CreateAsync();
        await using var _ = connection;
        await using var __ = db;
        db.PriceRules.AddRange(
            new PriceRule { Id = 1, BusinessId = 1, Name = "Mine", RuleType = "global", DiscountType = "percentage", DiscountValue = 10m, IsActive = true },
            new PriceRule { Id = 2, BusinessId = 2, Name = "Theirs", RuleType = "global", DiscountType = "percentage", DiscountValue = 90m, IsActive = true },
            new PriceRule { Id = 3, BusinessId = 2, Name = "Theirs variant", RuleType = "variant", TargetId = 2, DiscountType = "percentage", DiscountValue = 50m, IsActive = true });
        db.OutletPriceOverrides.AddRange(
            new OutletPriceOverride { Id = 1, BusinessId = 1, OutletId = 11, ProductVariantId = 1, OverrideType = "fixed", OverrideValue = 70m, IsActive = true },
            new OutletPriceOverride { Id = 2, BusinessId = 2, OutletId = 22, ProductVariantId = 2, OverrideType = "fixed", OverrideValue = 5m, IsActive = true });
        await db.SaveChangesAsync();

        var asOf = DateTime.UtcNow;
        var mine = await new PosCartPricingStore(db, new TestBusinessScope(1))
            .LoadPricingDataAsync([1, 2], [11, 22], asOf, CancellationToken.None);
        Assert.Equal([1L], mine.Variants.Select(variant => variant.Id).ToArray());
        Assert.Equal([1L], mine.Rules.Select(rule => rule.Id).ToArray());
        Assert.Equal([11L], mine.Overrides.Select(o => o.OutletId).ToArray());

        var theirs = await new PosCartPricingStore(db, new TestBusinessScope(2))
            .LoadPricingDataAsync([1, 2], [11, 22], asOf, CancellationToken.None);
        Assert.Equal([2L, 3L], theirs.Rules.Select(rule => rule.Id).Order().ToArray());
        Assert.Equal([22L], theirs.Overrides.Select(o => o.OutletId).ToArray());
    }

    private sealed class AllowAll : RetailPOS.Application.Pricing.Abstractions.IOutletPriceOverrideWriteAuthorizer
    {
        public Task<long> AuthorizeWriteAsync(long outletId, CancellationToken cancellationToken) => Task.FromResult(outletId);
    }
}
