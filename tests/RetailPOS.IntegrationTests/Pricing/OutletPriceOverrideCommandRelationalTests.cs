using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Pricing.Commands;
using RetailPOS.Application.Pricing.ReadModels;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Pricing;

namespace RetailPOS.Tests.Pricing;

public sealed class OutletPriceOverrideCommandRelationalTests
{
    [Fact]
    public async Task CommandsPersistRowsAndEnforceOnlyActiveOutletVariantDuplicates()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var dbContext = new RetailPOSDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();
        await SeedTargetsAsync(dbContext);

        var store = new OutletPriceOverrideCommandStore(dbContext, new TestBusinessScope(1));
        var authorizer = new AllowAllAuthorizer();
        var createHandler = new CreateOutletPriceOverrideCommandHandler(store, authorizer);
        var updateHandler = new UpdateOutletPriceOverrideCommandHandler(store, authorizer);
        var deleteHandler = new DeleteOutletPriceOverrideCommandHandler(store, authorizer);

        var created = await createHandler.Handle(
            new CreateOutletPriceOverrideCommand(
                new OutletPriceOverrideWriteData(5001, 9801, "FIXED", 25m, true)),
            CancellationToken.None);
        Assert.Equal("fixed", created.OverrideType);
        Assert.Equal("Outlet", created.OutletName);
        Assert.Equal("Variant", created.VariantName);
        Assert.Equal("Product", created.ProductName);

        var duplicate = await Assert.ThrowsAsync<InvalidOperationException>(
            () => createHandler.Handle(
                new CreateOutletPriceOverrideCommand(
                    new OutletPriceOverrideWriteData(5001, 9801, "fixed", 30m, true)),
                CancellationToken.None));
        Assert.Contains("An override already exists", duplicate.Message);
        Assert.Equal(1, await dbContext.OutletPriceOverrides.AsNoTracking().CountAsync());

        var updated = await updateHandler.Handle(
            new UpdateOutletPriceOverrideCommand(created.Id, "MARGIN", 15m, false),
            CancellationToken.None);
        Assert.Equal("margin", updated.OverrideType);
        Assert.Equal(15m, updated.OverrideValue);
        Assert.False(updated.IsActive);

        var replacement = await createHandler.Handle(
            new CreateOutletPriceOverrideCommand(
                new OutletPriceOverrideWriteData(5001, 9801, "fixed", 31m, true)),
            CancellationToken.None);
        Assert.NotEqual(created.Id, replacement.Id);
        Assert.Equal(2, await dbContext.OutletPriceOverrides.AsNoTracking().CountAsync());

        await deleteHandler.Handle(
            new DeleteOutletPriceOverrideCommand(created.Id),
            CancellationToken.None);
        Assert.Single(await dbContext.OutletPriceOverrides.AsNoTracking().ToListAsync());
        Assert.NotNull(await store.GetByIdAsync(replacement.Id, CancellationToken.None));
    }

    private static async Task SeedTargetsAsync(RetailPOSDbContext dbContext)
    {
        dbContext.Businesses.Add(new Business { Id = 1, Name = "Business", IsActive = true });
        dbContext.Categories.Add(new Category { Id = 9001, BusinessId = 1, Name = "Category" });
        dbContext.Outlets.Add(new Outlet { Id = 5001, BusinessId = 1, Name = "Outlet" });
        dbContext.Products.Add(new Product
        {
            Id = 9101,
            BusinessId = 1,
            Name = "Product",
            CategoryId = 9001,
            BasePrice = 20m
        });
        dbContext.ProductVariants.Add(new ProductVariant
        {
            Id = 9801,
            BusinessId = 1,
            ProductId = 9101,
            Name = "Variant",
            Sku = "TEST-VARIANT"
        });
        await dbContext.SaveChangesAsync();
    }

    private sealed class AllowAllAuthorizer : RetailPOS.Application.Pricing.Abstractions.IOutletPriceOverrideWriteAuthorizer
    {
        public Task<long> AuthorizeWriteAsync(long outletId, CancellationToken cancellationToken) =>
            Task.FromResult(outletId);
    }
}
