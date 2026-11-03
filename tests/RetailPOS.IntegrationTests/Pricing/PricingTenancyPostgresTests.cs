using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Pricing;

/// <summary>
/// Phase 2.1 Workstream C on a real PostgreSQL schema built from the tracked migration chain (isolated Test stack only):
/// price rules and outlet overrides belong to exactly one Business, the database refuses cross-Business links,
/// and the upgrade backfill never guesses an owner.
/// </summary>
public sealed class PricingTenancyPostgresTests
{
    private const string Phase2Migration = "20261102181136_PosV2P2TenancyTaxSettingsInvoiceNumbering";

    private static Task RunAsync(Func<PostgresTestDatabase, Task> operation) =>
        PostgresTestDatabase.RunAsync(
            InfrastructureTestSettings.RequireIsolatedPostgres(),
            PostgresTestDatabasePrefix.SaleCreation,
            operation);

    private static RetailPOSDbContext Context(PostgresTestDatabase database) =>
        new(new DbContextOptionsBuilder<RetailPOSDbContext>().UseNpgsql(database.ConnectionString).Options);

    private static async Task ExecuteAsync(PostgresTestDatabase database, string sql)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<List<string?>> ColumnAsync(PostgresTestDatabase database, string sql)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var values = new List<string?>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            values.Add(reader.IsDBNull(0) ? null : Convert.ToString(reader.GetValue(0), System.Globalization.CultureInfo.InvariantCulture));
        return values;
    }

    private static async Task SeedTwoBusinessesAsync(RetailPOSDbContext db)
    {
        db.Businesses.AddRange(new Business { Id = 1, Name = "One" }, new Business { Id = 2, Name = "Two" });
        await db.SaveChangesAsync();
        db.Categories.AddRange(
            new Category { Id = 1, BusinessId = 1, Name = "Cat 1" },
            new Category { Id = 2, BusinessId = 2, Name = "Cat 2" });
        db.Outlets.AddRange(
            new Outlet { Id = 11, BusinessId = 1, Name = "Outlet 1", InvoicePrefix = "AAA" },
            new Outlet { Id = 22, BusinessId = 2, Name = "Outlet 2", InvoicePrefix = "BBB" });
        await db.SaveChangesAsync();
        db.Products.AddRange(
            new Product { Id = 1, BusinessId = 1, CategoryId = 1, Name = "P1", BasePrice = 10m, CostPrice = 1m },
            new Product { Id = 2, BusinessId = 2, CategoryId = 2, Name = "P2", BasePrice = 10m, CostPrice = 1m });
        await db.SaveChangesAsync();
        db.ProductVariants.AddRange(
            new ProductVariant { Id = 1, BusinessId = 1, ProductId = 1, Name = "V1", Sku = "S1" },
            new ProductVariant { Id = 2, BusinessId = 2, ProductId = 2, Name = "V2", Sku = "S2" });
        await db.SaveChangesAsync();
    }

    private static PriceRule Rule(long id, long businessId, string ruleType, long? targetId) => new()
    {
        Id = id,
        BusinessId = businessId,
        Name = $"Rule {id}",
        RuleType = ruleType,
        TargetId = targetId,
        DiscountType = "percentage",
        DiscountValue = 10m,
        IsActive = true
    };

    [Fact]
    public Task Database_RejectsOutletOverridesThatMixBusinesses() => RunAsync(async database =>
    {
        await using var db = Context(database);
        await db.Database.MigrateAsync();
        await SeedTwoBusinessesAsync(db);

        db.OutletPriceOverrides.Add(new OutletPriceOverride
        {
            BusinessId = 1, OutletId = 11, ProductVariantId = 1, OverrideType = "fixed", OverrideValue = 5m
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        db.OutletPriceOverrides.Add(new OutletPriceOverride
        {
            BusinessId = 1, OutletId = 22, ProductVariantId = 1, OverrideType = "fixed", OverrideValue = 5m
        });
        var foreignOutlet = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, ((PostgresException)foreignOutlet.InnerException!).SqlState);
        db.ChangeTracker.Clear();

        db.OutletPriceOverrides.Add(new OutletPriceOverride
        {
            BusinessId = 1, OutletId = 11, ProductVariantId = 2, OverrideType = "fixed", OverrideValue = 5m
        });
        var foreignVariant = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, ((PostgresException)foreignVariant.InnerException!).SqlState);
        db.ChangeTracker.Clear();

        db.OutletPriceOverrides.Add(new OutletPriceOverride
        {
            BusinessId = 2, OutletId = 22, ProductVariantId = 2, OverrideType = "fixed", OverrideValue = 7m
        });
        await db.SaveChangesAsync();
        Assert.Equal(2, await db.OutletPriceOverrides.AsNoTracking().CountAsync());
    });

    [Fact]
    public Task Database_RejectsPriceRulesTargetingAnotherBusiness() => RunAsync(async database =>
    {
        await using var db = Context(database);
        await db.Database.MigrateAsync();
        await SeedTwoBusinessesAsync(db);

        foreach (var (ruleType, foreignTarget, ownTarget) in new (string, long, long)[]
                 {
                     ("variant", 2, 1), ("product", 2, 1), ("category", 2, 1), ("outlet", 22, 11)
                 })
        {
            db.PriceRules.Add(Rule(100, 1, ruleType, foreignTarget));
            var failure = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.CheckViolation, ((PostgresException)failure.InnerException!).SqlState);
            db.ChangeTracker.Clear();

            db.PriceRules.Add(Rule(101, 1, ruleType, ownTarget));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            await db.PriceRules.Where(rule => rule.Id == 101).ExecuteDeleteAsync();
        }

        db.PriceRules.Add(Rule(102, 1, "global", null));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        // Re-pointing an existing rule at another Business's target is refused too.
        db.PriceRules.Add(Rule(103, 1, "variant", 1));
        await db.SaveChangesAsync();
        var tracked = await db.PriceRules.SingleAsync(rule => rule.Id == 103);
        tracked.TargetId = 2;
        var update = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, ((PostgresException)update.InnerException!).SqlState);
    });

    [Fact]
    public Task Upgrade_BackfillsRulesAndOverridesOfASingleBusinessDatabase() => RunAsync(async database =>
    {
        await using var db = Context(database);
        await db.GetService<IMigrator>().MigrateAsync(Phase2Migration);
        await ExecuteAsync(database, """
            INSERT INTO businesses (id, name, is_active, created_at, updated_at) VALUES (1, 'Legacy', true, now(), now());
            INSERT INTO outlets (id, business_id, name, address, invoice_prefix, created_at, updated_at, source_version)
                VALUES (1, 1, 'Main', 'a', 'AAA', now(), now(), 1);
            INSERT INTO categories (id, business_id, name, display_order, is_active, created_at, updated_at)
                VALUES (1, 1, 'Cat', 0, true, now(), now());
            INSERT INTO products (id, business_id, name, category_id, base_price, cost_price, has_variants, created_at, updated_at)
                VALUES (1, 1, 'P', 1, 10, 5, false, now(), now());
            INSERT INTO product_variants (id, business_id, product_id, name, sku, attributes, price_adjustment, cost_adjustment, created_at, updated_at)
                VALUES (1, 1, 1, 'V', 'SKU1', '{}', 0, 0, now(), now());
            INSERT INTO price_rules (id, name, rule_type, discount_type, discount_value, min_quantity, priority, is_active, created_at, updated_at)
                VALUES (1, 'Legacy rule', 'global', 'percentage', 10, 1, 0, true, now(), now());
            INSERT INTO outlet_price_overrides (id, outlet_id, product_variant_id, override_type, override_value, is_active, created_at, updated_at)
                VALUES (1, 1, 1, 'fixed', 9, true, now(), now());
            """);

        await db.Database.MigrateAsync();

        Assert.Equal(new string?[] { "1" }, await ColumnAsync(database, "SELECT business_id FROM price_rules"));
        Assert.Equal(new string?[] { "1" }, await ColumnAsync(database, "SELECT business_id FROM outlet_price_overrides"));
        Assert.Equal(
            new string?[] { "NO", "NO" },
            await ColumnAsync(
                database,
                "SELECT is_nullable FROM information_schema.columns WHERE column_name = 'business_id' AND table_name IN ('price_rules','outlet_price_overrides') ORDER BY table_name"));
    });

    [Fact]
    public Task Upgrade_DerivesOverrideOwnerFromItsOutletWhenSeveralBusinessesExist() => RunAsync(async database =>
    {
        await using var db = Context(database);
        await db.GetService<IMigrator>().MigrateAsync(Phase2Migration);
        await ExecuteAsync(database, """
            INSERT INTO businesses (id, name, is_active, created_at, updated_at) VALUES (1, 'A', true, now(), now()), (2, 'B', true, now(), now());
            INSERT INTO outlets (id, business_id, name, address, invoice_prefix, created_at, updated_at, source_version)
                VALUES (1, 1, 'A main', 'a', 'AAA', now(), now(), 1), (2, 2, 'B main', 'b', 'BBB', now(), now(), 1);
            INSERT INTO categories (id, business_id, name, display_order, is_active, created_at, updated_at)
                VALUES (1, 1, 'Cat A', 0, true, now(), now()), (2, 2, 'Cat B', 0, true, now(), now());
            INSERT INTO products (id, business_id, name, category_id, base_price, cost_price, has_variants, created_at, updated_at)
                VALUES (1, 1, 'PA', 1, 10, 5, false, now(), now()), (2, 2, 'PB', 2, 10, 5, false, now(), now());
            INSERT INTO product_variants (id, business_id, product_id, name, sku, attributes, price_adjustment, cost_adjustment, created_at, updated_at)
                VALUES (1, 1, 1, 'VA', 'SKU1', '{}', 0, 0, now(), now()), (2, 2, 2, 'VB', 'SKU2', '{}', 0, 0, now(), now());
            INSERT INTO outlet_price_overrides (id, outlet_id, product_variant_id, override_type, override_value, is_active, created_at, updated_at)
                VALUES (1, 1, 1, 'fixed', 9, true, now(), now()), (2, 2, 2, 'fixed', 8, true, now(), now());
            """);

        await db.Database.MigrateAsync();

        Assert.Equal(new string?[] { "1", "2" }, await ColumnAsync(database, "SELECT business_id FROM outlet_price_overrides ORDER BY id"));
    });

    [Fact]
    public Task Upgrade_RefusesToGuessTheOwnerOfRulesWhenSeveralBusinessesExist() => RunAsync(async database =>
    {
        await using var db = Context(database);
        await db.GetService<IMigrator>().MigrateAsync(Phase2Migration);
        await ExecuteAsync(database, """
            INSERT INTO businesses (id, name, is_active, created_at, updated_at) VALUES (1, 'A', true, now(), now()), (2, 'B', true, now(), now());
            INSERT INTO price_rules (id, name, rule_type, discount_type, discount_value, min_quantity, priority, is_active, created_at, updated_at)
                VALUES (1, 'Ownerless rule', 'global', 'percentage', 10, 1, 0, true, now(), now());
            """);

        var failure = await Assert.ThrowsAnyAsync<Exception>(() => db.Database.MigrateAsync());

        Assert.Contains("more than one Business exists", failure.ToString(), StringComparison.Ordinal);
    });

    [Fact]
    public Task Upgrade_RefusesAnOverrideWhoseOutletAndVariantBelongToDifferentBusinesses() => RunAsync(async database =>
    {
        await using var db = Context(database);
        await db.GetService<IMigrator>().MigrateAsync(Phase2Migration);
        await ExecuteAsync(database, """
            INSERT INTO businesses (id, name, is_active, created_at, updated_at) VALUES (1, 'A', true, now(), now()), (2, 'B', true, now(), now());
            INSERT INTO outlets (id, business_id, name, address, invoice_prefix, created_at, updated_at, source_version)
                VALUES (1, 1, 'A main', 'a', 'AAA', now(), now(), 1);
            INSERT INTO categories (id, business_id, name, display_order, is_active, created_at, updated_at)
                VALUES (2, 2, 'Cat B', 0, true, now(), now());
            INSERT INTO products (id, business_id, name, category_id, base_price, cost_price, has_variants, created_at, updated_at)
                VALUES (2, 2, 'PB', 2, 10, 5, false, now(), now());
            INSERT INTO product_variants (id, business_id, product_id, name, sku, attributes, price_adjustment, cost_adjustment, created_at, updated_at)
                VALUES (2, 2, 2, 'VB', 'SKU2', '{}', 0, 0, now(), now());
            INSERT INTO outlet_price_overrides (id, outlet_id, product_variant_id, override_type, override_value, is_active, created_at, updated_at)
                VALUES (1, 1, 2, 'fixed', 9, true, now(), now());
            """);

        var failure = await Assert.ThrowsAnyAsync<Exception>(() => db.Database.MigrateAsync());

        Assert.Contains("different or unknown Businesses", failure.ToString(), StringComparison.Ordinal);
    });
}
