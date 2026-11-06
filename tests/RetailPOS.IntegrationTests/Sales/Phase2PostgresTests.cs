using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Application.Sales.Checkout;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Sales;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Sales;

/// <summary>
/// Phase 2 on a real PostgreSQL schema built from the tracked migration chain (isolated Test stack only): the D13 allocator,
/// tenant-integrity constraints, and the upgrade backfill of legacy data.
/// </summary>
public sealed class Phase2PostgresTests
{
    private const string Phase1Migration = "20261102162334_PosV2P1CheckoutPaymentEvidenceAndIdempotency";

    private static Task RunAsync(Func<PostgresTestDatabase, Task> operation) =>
        PostgresTestDatabase.RunAsync(
            InfrastructureTestSettings.RequireIsolatedPostgres(),
            PostgresTestDatabasePrefix.SaleCreation,
            PostgresTestDatabaseStrategy.FreshMigration,
            operation);

    private static RetailPOSDbContext Context(PostgresTestDatabase database) =>
        new(new DbContextOptionsBuilder<RetailPOSDbContext>().UseNpgsql(database.ConnectionString).Options);

    private static async Task MigrateAsync(RetailPOSDbContext db) => await db.Database.MigrateAsync();

    private static async Task SeedOutletAsync(RetailPOSDbContext db, string prefix = "DHK")
    {
        db.Businesses.Add(new Business { Id = 1, Name = "One" });
        await db.SaveChangesAsync();
        db.Outlets.AddRange(
            new Outlet { Id = 11, BusinessId = 1, Name = "Main", InvoicePrefix = prefix },
            new Outlet { Id = 12, BusinessId = 1, Name = "Branch", InvoicePrefix = prefix + "B" });
        await db.SaveChangesAsync();
    }

    private static async Task<AllocatedInvoiceNumber> AllocateInOwnTransactionAsync(
        PostgresTestDatabase database, long outletId, DateTime date, bool commit = true)
    {
        await using var db = Context(database);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var allocated = await new SaleInvoiceNumberAllocator(db).AllocateAsync(1, outletId, date, CancellationToken.None);
        if (commit)
            await transaction.CommitAsync();
        else
            await transaction.RollbackAsync();
        return allocated;
    }

    [Fact]
    public Task Allocator_ConcurrentSalesOfOneOutletAndDayGetDistinctGaplessSequences() => RunAsync(async database =>
    {
        await using (var setup = Context(database))
        {
            await MigrateAsync(setup);
            await SeedOutletAsync(setup);
        }

        var date = new DateTime(2026, 11, 2);
        var results = await Task.WhenAll(Enumerable.Range(0, 24)
            .Select(_ => Task.Run(() => AllocateInOwnTransactionAsync(database, 11, date))));

        Assert.Equal(Enumerable.Range(1, 24).Select(number => (long)number), results.Select(r => r.Sequence).Order());
        Assert.Equal(24, results.Select(r => r.InvoiceNumber).Distinct().Count());
        Assert.All(results, r => Assert.Equal($"DHK261102{r.Sequence}", r.InvoiceNumber));
        Assert.All(results, r => Assert.Equal("DHK", r.Prefix));
    });

    [Fact]
    public Task Allocator_ResetsPerBusinessDateAndIsIndependentPerOutlet() => RunAsync(async database =>
    {
        await using (var setup = Context(database))
        {
            await MigrateAsync(setup);
            await SeedOutletAsync(setup);
        }

        var day1 = new DateTime(2026, 11, 2);
        var first = await AllocateInOwnTransactionAsync(database, 11, day1);
        var second = await AllocateInOwnTransactionAsync(database, 11, day1);
        var nextDay = await AllocateInOwnTransactionAsync(database, 11, day1.AddDays(1));
        var otherOutlet = await AllocateInOwnTransactionAsync(database, 12, day1);
        var backToDay1 = await AllocateInOwnTransactionAsync(database, 11, day1);

        Assert.Equal(new long[] { 1, 2, 1, 1, 3 }, new[] { first, second, nextDay, otherOutlet, backToDay1 }.Select(r => r.Sequence));
        Assert.Equal("DHK2611021", first.InvoiceNumber);
        Assert.Equal("DHK2611031", nextDay.InvoiceNumber);
        Assert.Equal("DHKB2611021", otherOutlet.InvoiceNumber);
    });

    [Fact]
    public Task Allocator_ARolledBackSaleGivesItsNumberBack() => RunAsync(async database =>
    {
        await using (var setup = Context(database))
        {
            await MigrateAsync(setup);
            await SeedOutletAsync(setup);
        }

        var date = new DateTime(2026, 11, 2);
        var abandoned = await AllocateInOwnTransactionAsync(database, 11, date, commit: false);
        var issued = await AllocateInOwnTransactionAsync(database, 11, date);

        Assert.Equal(1, abandoned.Sequence);
        Assert.Equal(1, issued.Sequence);
    });

    [Fact]
    public Task Allocator_RefusesAnOutletWithoutAPrefixAndRequiresATransaction() => RunAsync(async database =>
    {
        await using var db = Context(database);
        await MigrateAsync(db);
        db.Businesses.Add(new Business { Id = 1, Name = "One" });
        await db.SaveChangesAsync();
        db.Outlets.Add(new Outlet { Id = 11, BusinessId = 1, Name = "No prefix" });
        await db.SaveChangesAsync();

        var allocator = new SaleInvoiceNumberAllocator(db);
        var missing = await Assert.ThrowsAsync<SaleCheckoutValidationException>(
            () => allocator.AllocateAsync(1, 11, DateTime.UtcNow, CancellationToken.None));
        Assert.Equal(SaleCheckoutErrorCodes.OutletInvoicePrefixMissing, missing.Code);

        db.Outlets.Single().InvoicePrefix = "DHK";
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => allocator.AllocateAsync(1, 11, DateTime.UtcNow, CancellationToken.None));
    });

    [Fact]
    public Task Database_RejectsCrossBusinessCatalogueLinks() => RunAsync(async database =>
    {
        await using var db = Context(database);
        await MigrateAsync(db);
        db.Businesses.AddRange(new Business { Id = 1, Name = "One" }, new Business { Id = 2, Name = "Two" });
        await db.SaveChangesAsync();
        db.Categories.AddRange(
            new Category { Id = 1, BusinessId = 1, Name = "Cat 1" },
            new Category { Id = 2, BusinessId = 2, Name = "Cat 2" });
        db.Products.Add(new Product { Id = 1, BusinessId = 1, CategoryId = 1, Name = "P1", BasePrice = 1m, CostPrice = 1m });
        await db.SaveChangesAsync();

        // Product of Business 1 pointing at Business 2's category.
        db.Products.Add(new Product { Id = 2, BusinessId = 1, CategoryId = 2, Name = "P2", BasePrice = 1m, CostPrice = 1m });
        var product = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, ((PostgresException)product.InnerException!).SqlState);
        db.ChangeTracker.Clear();

        // Variant of Business 2 under Business 1's product.
        db.ProductVariants.Add(new ProductVariant { Id = 1, BusinessId = 2, ProductId = 1, Name = "V", Sku = "V1" });
        var variant = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, ((PostgresException)variant.InnerException!).SqlState);
        db.ChangeTracker.Clear();

        // Child category under another Business's parent.
        db.Categories.Add(new Category { Id = 3, BusinessId = 1, ParentCategoryId = 2, Name = "Child" });
        var category = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, ((PostgresException)category.InnerException!).SqlState);
    });

    [Fact]
    public Task Database_AllowsTheSameSkuBarcodeAndCustomerCodeInDifferentBusinessesButNotWithinOne() => RunAsync(async database =>
    {
        await using var db = Context(database);
        await MigrateAsync(db);
        db.Businesses.AddRange(new Business { Id = 1, Name = "One" }, new Business { Id = 2, Name = "Two" });
        await db.SaveChangesAsync();
        db.Categories.AddRange(new Category { Id = 1, BusinessId = 1, Name = "C1" }, new Category { Id = 2, BusinessId = 2, Name = "C2" });
        db.Products.AddRange(
            new Product { Id = 1, BusinessId = 1, CategoryId = 1, Name = "P1", ProductCode = "CODE", Barcode = "BAR", BasePrice = 1m, CostPrice = 1m },
            new Product { Id = 2, BusinessId = 2, CategoryId = 2, Name = "P2", ProductCode = "CODE", Barcode = "BAR", BasePrice = 1m, CostPrice = 1m });
        db.ProductVariants.AddRange(
            new ProductVariant { Id = 1, BusinessId = 1, ProductId = 1, Name = "V1", Sku = "SKU", Barcode = "VBAR" },
            new ProductVariant { Id = 2, BusinessId = 2, ProductId = 2, Name = "V2", Sku = "SKU", Barcode = "VBAR" });
        db.Customers.AddRange(
            new Customer { Id = 1, BusinessId = 1, Name = Customer.WalkInCustomerName, CustomerCode = Customer.WalkInCustomerCode, IsSystem = true },
            new Customer { Id = 2, BusinessId = 2, Name = Customer.WalkInCustomerName, CustomerCode = Customer.WalkInCustomerCode, IsSystem = true },
            new Customer { Id = 3, BusinessId = 1, Name = "A", Phone = "01712345678", PhoneNormalized = "+8801712345678" },
            new Customer { Id = 4, BusinessId = 2, Name = "B", Phone = "01712345678", PhoneNormalized = "+8801712345678" });
        await db.SaveChangesAsync();

        db.ProductVariants.Add(new ProductVariant { Id = 3, BusinessId = 1, ProductId = 1, Name = "Dup", Sku = "SKU" });
        var sku = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, ((PostgresException)sku.InnerException!).SqlState);
        db.ChangeTracker.Clear();

        db.Customers.Add(new Customer { Id = 5, BusinessId = 1, Name = "Dup phone", Phone = "+880 1712-345678", PhoneNormalized = "+8801712345678" });
        var phone = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, ((PostgresException)phone.InnerException!).SqlState);
        db.ChangeTracker.Clear();

        db.Customers.Add(new Customer { Id = 6, BusinessId = 1, Name = "Second walk-in", CustomerCode = Customer.WalkInCustomerCode });
        var walkIn = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, ((PostgresException)walkIn.InnerException!).SqlState);
    });

    [Fact]
    public Task Database_RejectsSalesThatMixBusinessesAndDuplicateInvoiceIdentities() => RunAsync(async database =>
    {
        await using var db = Context(database);
        await MigrateAsync(db);
        db.Businesses.AddRange(new Business { Id = 1, Name = "One" }, new Business { Id = 2, Name = "Two" });
        await db.SaveChangesAsync();
        db.Outlets.AddRange(
            new Outlet { Id = 11, BusinessId = 1, Name = "Main", InvoicePrefix = "A" },
            new Outlet { Id = 21, BusinessId = 2, Name = "Other", InvoicePrefix = "B" });
        db.Users.Add(new User { Id = 5, BusinessId = 1, Name = "Cashier", Email = "c@example.test", PasswordHash = "x", IsActive = true });
        db.Categories.AddRange(new Category { Id = 1, BusinessId = 1, Name = "C1" }, new Category { Id = 2, BusinessId = 2, Name = "C2" });
        db.Products.AddRange(
            new Product { Id = 1, BusinessId = 1, CategoryId = 1, Name = "P1", BasePrice = 1m, CostPrice = 1m },
            new Product { Id = 2, BusinessId = 2, CategoryId = 2, Name = "P2", BasePrice = 1m, CostPrice = 1m });
        db.ProductVariants.AddRange(
            new ProductVariant { Id = 1, BusinessId = 1, ProductId = 1, Name = "V1", Sku = "V1" },
            new ProductVariant { Id = 2, BusinessId = 2, ProductId = 2, Name = "V2", Sku = "V2" });
        db.Customers.AddRange(
            new Customer { Id = 1, BusinessId = 1, Name = "Mine" },
            new Customer { Id = 2, BusinessId = 2, Name = "Theirs" });
        await db.SaveChangesAsync();

        Sale NewSale(long id, long outletId, long? customerId, string number, long? sequence = null) => new()
        {
            Id = id, BusinessId = 1, OutletId = outletId, CustomerId = customerId, CashierId = 5, SaleNumber = number,
            PaymentMethod = "cash", Status = "completed", SaleDate = DateTime.UtcNow, BusinessDate = new DateTime(2026, 11, 2),
            InvoicePrefix = sequence.HasValue ? "A" : null, InvoiceSequence = sequence
        };

        db.Sales.Add(NewSale(1, 11, 2, "A2611021", 1));
        var customer = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, ((PostgresException)customer.InnerException!).SqlState);
        db.ChangeTracker.Clear();

        db.Sales.Add(NewSale(2, 21, 1, "A2611022", 2));
        var outlet = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, ((PostgresException)outlet.InnerException!).SqlState);
        db.ChangeTracker.Clear();

        db.Sales.Add(NewSale(3, 11, 1, "A2611021", 1));
        await db.SaveChangesAsync();

        db.Sales.Add(NewSale(4, 11, 1, "A2611021"));
        var duplicateNumber = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, ((PostgresException)duplicateNumber.InnerException!).SqlState);
        db.ChangeTracker.Clear();

        db.Sales.Add(NewSale(5, 11, 1, "A2611029", 1));
        var duplicateSequence = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, ((PostgresException)duplicateSequence.InnerException!).SqlState);
        db.ChangeTracker.Clear();

        db.SaleItems.Add(new SaleItem { SaleId = 3, VariantId = 2, Quantity = 1, UnitPrice = 1m, Subtotal = 1m });
        var item = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, ((PostgresException)item.InnerException!).SqlState);
    });

    [Fact]
    public Task Upgrade_BackfillsASingleBusinessDatabaseWithoutLosingData() => RunAsync(async database =>
    {
        await using var db = Context(database);
        await db.GetService<IMigrator>().MigrateAsync(Phase1Migration);

        await using (var connection = new NpgsqlConnection(database.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO businesses (id, name, is_active, created_at, updated_at) VALUES (1, 'Legacy', true, now(), now());
                INSERT INTO outlets (id, business_id, name, address, created_at, updated_at, source_version) VALUES
                    (1, 1, 'Main', 'a', now(), now(), 1), (2, 1, 'Branch', 'b', now(), now(), 1);
                INSERT INTO categories (id, name, display_order, is_active, created_at, updated_at) VALUES (1, 'Cat', 0, true, now(), now());
                INSERT INTO products (id, name, category_id, base_price, cost_price, tax_rate, has_variants, created_at, updated_at) VALUES
                    (1, 'Zero', 1, 10, 5, 0, false, now(), now()),
                    (2, 'Eight', 1, 10, 5, 8.5, false, now(), now());
                INSERT INTO product_variants (id, product_id, name, sku, attributes, price_adjustment, cost_adjustment, created_at, updated_at) VALUES
                    (1, 1, 'V1', 'SKU1', '{}', 0, 0, now(), now()), (2, 2, 'V2', 'SKU2', '{}', 0, 0, now(), now());
                INSERT INTO customers (id, name, customer_code, is_system, is_active, loyalty_points, created_at) VALUES (1, 'Walk-in Customer', 'WALKIN', true, true, 0, now());
                INSERT INTO customers (id, name, phone, is_system, is_active, loyalty_points, created_at) VALUES
                    (2, 'First', '01712345678', false, true, 0, now()),
                    (3, 'Same number', '+880 1712-345678', false, true, 0, now()),
                    (4, 'Legacy phone', '555-0142', false, true, 0, now()),
                    (5, 'International', '+1 (555) 100-1234', false, true, 0, now());
                """;
            await command.ExecuteNonQueryAsync();
        }

        await db.Database.MigrateAsync();

        await using var verify = new NpgsqlConnection(database.ConnectionString);
        await verify.OpenAsync();
        async Task<List<string?>> Column(string sql)
        {
            await using var command = verify.CreateCommand();
            command.CommandText = sql;
            var values = new List<string?>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                values.Add(reader.IsDBNull(0) ? null : Convert.ToString(reader.GetValue(0), System.Globalization.CultureInfo.InvariantCulture));
            return values;
        }

        Assert.Empty(await Column("SELECT 1 FROM categories WHERE business_id <> 1"));
        Assert.Empty(await Column("SELECT 1 FROM products WHERE business_id <> 1"));
        Assert.Empty(await Column("SELECT 1 FROM product_variants WHERE business_id <> 1"));
        Assert.Empty(await Column("SELECT 1 FROM customers WHERE business_id <> 1"));
        Assert.Equal(new string?[] { null, "8.50" }, await Column("SELECT tax_rate FROM products ORDER BY id"));
        Assert.Equal(
            new string?[] { null, "+8801712345678", null, null, "+15551001234" },
            await Column("SELECT phone_normalized FROM customers ORDER BY id"));
        Assert.Equal(new string?[] { "O001", "O002" }, await Column("SELECT invoice_prefix FROM outlets ORDER BY id"));
        Assert.Equal(5, (await Column("SELECT id FROM customers")).Count);
        Assert.Equal(2, (await Column("SELECT id FROM product_variants")).Count);
    });

    [Fact]
    public Task Upgrade_RefusesToGuessTheOwnerWhenSeveralBusinessesAlreadyHoldCatalogueData() => RunAsync(async database =>
    {
        await using var db = Context(database);
        await db.GetService<IMigrator>().MigrateAsync(Phase1Migration);
        await using (var connection = new NpgsqlConnection(database.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO businesses (id, name, is_active, created_at, updated_at) VALUES (1, 'A', true, now(), now()), (2, 'B', true, now(), now());
                INSERT INTO categories (id, name, display_order, is_active, created_at, updated_at) VALUES (1, 'Cat', 0, true, now(), now());
                """;
            await command.ExecuteNonQueryAsync();
        }

        var failure = await Assert.ThrowsAnyAsync<Exception>(() => db.Database.MigrateAsync());

        Assert.Contains("exactly one Business", failure.ToString(), StringComparison.Ordinal);
    });
}
