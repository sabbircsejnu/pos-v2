using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Sales;

namespace RetailPOS.IntegrationTests.Sales;

public sealed class HeldSaleStoreRelationalTests : IAsyncLifetime
{
    private readonly string _connectionString =
        $"Data Source=held-sale-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Foreign Keys=True";
    private SqliteConnection _anchorConnection = null!;

    public async Task InitializeAsync()
    {
        _anchorConnection = new SqliteConnection(_connectionString);
        await _anchorConnection.OpenAsync();
        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
        context.Businesses.AddRange(
            new Business { Id = 1, Name = "Business one" },
            new Business { Id = 2, Name = "Business two" });
        context.Outlets.AddRange(
            new Outlet { Id = 11, BusinessId = 1, Name = "Outlet one", Address = "Address one" },
            new Outlet { Id = 22, BusinessId = 2, Name = "Outlet two", Address = "Address two" });
        context.Users.AddRange(
            new User { Id = 7, BusinessId = 1, Name = "Cashier one", Email = "one@example.test" },
            new User { Id = 8, BusinessId = 2, Name = "Cashier two", Email = "two@example.test" });
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task Store_PersistsHeldSaleAndRestrictsCashierQueriesToAuthorizedOutlets()
    {
        await using var context = CreateContext();
        var store = new HeldSaleStore(context);
        var heldSale = await store.CreateAsync(
            new HeldSale
            {
                OutletId = 11,
                CashierId = 7,
                ItemsJson = "[]",
                PaymentMethod = "cash"
            },
            CancellationToken.None);

        Assert.True(heldSale.Id > 0);
        Assert.Equal("Outlet one", heldSale.Outlet.Name);
        Assert.Equal("Cashier one", heldSale.Cashier.Name);
        Assert.NotEqual(default, heldSale.HeldAt);

        var visible = await store.GetByCashierAsync(7, [11], CancellationToken.None);
        Assert.Equal(heldSale.Id, Assert.Single(visible).Id);
        Assert.Empty(await store.GetByCashierAsync(7, [], CancellationToken.None));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            store.CreateAsync(
                new HeldSale
                {
                    OutletId = 11,
                    CashierId = 8,
                    ItemsJson = "[]",
                    PaymentMethod = "cash"
                },
                CancellationToken.None));
    }

    [Fact]
    public async Task Delete_MissingHeldSaleRemainsIdempotent()
    {
        await using var context = CreateContext();
        var store = new HeldSaleStore(context);

        await store.DeleteAsync(999, CancellationToken.None);

        Assert.Empty(await context.HeldSales.ToListAsync());
    }

    public async Task DisposeAsync()
    {
        await _anchorConnection.DisposeAsync();
    }

    private RetailPOSDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseSqlite(_connectionString)
            .Options);
}
